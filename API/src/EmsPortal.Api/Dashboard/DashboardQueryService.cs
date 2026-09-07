using EmsPortal.Api.Models.Dashboard;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Api.Dashboard;

/// <summary>Builds the dashboard read models.</summary>
public sealed class DashboardQueryService : IDashboardQueryService
{
    private readonly EmsPortalDbContext _db;
    private readonly ITenantRepository _tenants;

    public DashboardQueryService(
        EmsPortalDbContext db,
        ITenantRepository tenants)
    {
        _db = db;
        _tenants = tenants;
    }

    // ---- Scoping helpers ----

    private IQueryable<User> UsersScoped(Guid? tenantId)
    {
        var q = _db.Users.IgnoreQueryFilters().Where(u => !u.Deleted);
        return tenantId is { } tid ? q.Where(u => u.TenantRoles.Any(r => r.TenantId == tid && !r.Deleted)) : q;
    }

    // ---- Users ----

    public async Task<UserDashboardDto> GetUsersAsync(Guid? tenantId, string dateRange, CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var users = await UsersScoped(tenantId)
            .Select(u => new { u.Id, u.MustChangePassword, u.CreatedOnUtc, u.IsActive })
            .ToListAsync(cancellationToken);

        // LoggedInToday / ActiveThisWeek / Inactive30Days are not trackable (no last-login timestamp): return 0.
        var kpis = new UserKpisDto(
            Total: users.Count,
            LoggedInToday: 0,
            ActiveThisWeek: 0,
            Inactive30Days: 0,
            PendingFirstLogin: users.Count(u => u.MustChangePassword),
            NewThisMonth: users.Count(u => u.CreatedOnUtc >= monthStart));

        var roleDistribution = await RoleDistributionAsync(tenantId, cancellationToken);

        // No user-activity audit source; an empty feed is acceptable for this WO.
        return new UserDashboardDto(kpis, roleDistribution, Array.Empty<ActivityEntry>());
    }

    private async Task<IReadOnlyList<RoleCount>> RoleDistributionAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var assignments = await _db.UserTenantRoles.IgnoreQueryFilters()
            .Where(r => !r.Deleted)
            .Where(r => tenantId == null || r.TenantId == tenantId)
            .Where(r => !r.User!.Deleted)
            .Select(r => new { r.UserId, RoleName = r.RoleEntity != null ? r.RoleEntity.Name : null, r.Role })
            .ToListAsync(cancellationToken);

        // One role label per user (first assignment wins); users with no assignment are "Unassigned".
        var allUsers = await UsersScoped(tenantId).Select(u => u.Id).ToListAsync(cancellationToken);
        var byUser = assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.First().RoleName ?? g.First().Role.ToString());

        var counts = new Dictionary<string, int>();
        foreach (var userId in allUsers)
        {
            var label = byUser.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Unassigned";
            counts[label] = counts.GetValueOrDefault(label) + 1;
        }
        return counts.Select(kv => new RoleCount(kv.Key, kv.Value)).OrderByDescending(r => r.Count).ToList();
    }

    // ---- Platform (Super Admin) ----

    public async Task<PlatformDashboardDto> GetPlatformAsync(string dateRange, bool forceRefresh, CancellationToken cancellationToken)
        => await BuildPlatformAsync(dateRange, cancellationToken);

    private async Task<PlatformDashboardDto> BuildPlatformAsync(string dateRange, CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tenants = await _tenants.ListAsync(cancellationToken);
        var tenantNames = tenants.ToDictionary(t => t.Id, t => t.Name);

        var activeTenants = tenants.Count(t => t.Status == TenantStatus.Active);
        var inactiveTenants = tenants.Count(t => t.Status == TenantStatus.Inactive);
        var archivedTenants = tenants.Count(t => t.Status == TenantStatus.Archived);

        var totalUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => !u.Deleted, cancellationToken);

        var tenantKpis = new TenantKpisDto(activeTenants, inactiveTenants, archivedTenants, totalUsers);

        var tenantHealth = await TenantHealthAsync(tenants, cancellationToken);
        var growth = await GrowthAsync(tenants, cancellationToken);
        var onboarding = await OnboardingAsync(tenants, cancellationToken);
        var userAnalytics = await PlatformUserAnalyticsAsync(tenantNames, monthStart, cancellationToken);

        return new PlatformDashboardDto(
            tenantKpis, tenantHealth, growth, onboarding, Array.Empty<SystemAlert>(), userAnalytics);
    }

    private async Task<IReadOnlyList<TenantHealthRow>> TenantHealthAsync(IReadOnlyList<Tenant> tenants, CancellationToken cancellationToken)
    {
        var rows = new List<TenantHealthRow>();
        foreach (var t in tenants)
        {
            var activeUsers = await UsersScoped(t.Id).CountAsync(u => u.IsActive, cancellationToken);
            rows.Add(new TenantHealthRow(t.Id, t.Name, activeUsers));
        }
        return rows;
    }

    private async Task<IReadOnlyList<GrowthPoint>> GrowthAsync(IReadOnlyList<Tenant> tenants, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.Date;
        var start = now.AddDays(-90);
        var userDates = await _db.Users.IgnoreQueryFilters().Where(u => !u.Deleted).Select(u => u.CreatedOnUtc).ToListAsync(cancellationToken);
        var tenantDates = tenants.Select(t => t.CreatedDate == default ? t.CreatedOnUtc : t.CreatedDate).ToList();

        var points = new List<GrowthPoint>();
        for (var d = start; d <= now; d = d.AddDays(1))
        {
            var dayEnd = d.AddDays(1);
            points.Add(new GrowthPoint(
                d.ToString("yyyy-MM-dd"),
                tenantDates.Count(td => td < dayEnd),
                userDates.Count(ud => ud < dayEnd)));
        }
        return points;
    }

    private async Task<IReadOnlyList<OnboardingRow>> OnboardingAsync(IReadOnlyList<Tenant> tenants, CancellationToken cancellationToken)
    {
        var rows = new List<OnboardingRow>();
        foreach (var t in tenants)
        {
            var missingUsers = !await UsersScoped(t.Id).AnyAsync(cancellationToken);
            rows.Add(new OnboardingRow(t.Id, t.Name, missingUsers));
        }
        return rows;
    }

    private async Task<PlatformUserAnalyticsDto> PlatformUserAnalyticsAsync(
        IReadOnlyDictionary<Guid, string> tenantNames, DateTime monthStart, CancellationToken cancellationToken)
    {
        var totalActive = await _db.Users.IgnoreQueryFilters().CountAsync(u => !u.Deleted && u.IsActive, cancellationToken);
        var pendingFirstLogin = await _db.Users.IgnoreQueryFilters().CountAsync(u => !u.Deleted && u.MustChangePassword, cancellationToken);
        var newThisMonth = await _db.Users.IgnoreQueryFilters().CountAsync(u => !u.Deleted && u.CreatedOnUtc >= monthStart, cancellationToken);

        // Users with no (non-deleted) tenant-role assignment.
        var noRole = await _db.Users.IgnoreQueryFilters()
            .CountAsync(u => !u.Deleted && !u.TenantRoles.Any(r => !r.Deleted), cancellationToken);

        // ByTenant: count of users per tenant via assignments.
        var byTenantRaw = await _db.UserTenantRoles.IgnoreQueryFilters()
            .Where(r => !r.Deleted && !r.User!.Deleted)
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var byTenant = byTenantRaw
            .Select(x => new TenantCount(tenantNames.GetValueOrDefault(x.TenantId, x.TenantId.ToString()), x.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        var growth = await GrowthAsync(await _tenants.ListAsync(cancellationToken), cancellationToken);

        // LoggedInToday not trackable (no last-login timestamp): 0. ActivityFeed empty (no user audit source).
        return new PlatformUserAnalyticsDto(
            totalActive, LoggedInToday: 0, pendingFirstLogin, noRole, newThisMonth, growth, byTenant, Array.Empty<ActivityEntry>());
    }
}
