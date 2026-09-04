using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives every other person on an individual client's return a generational particle of their own —
    /// the Suffix box now beside Last Name on the intake form's "Spouse &amp; More Individuals" card.
    /// <para>
    /// A related client is a client. The Related Entities list names them beside the client they were
    /// declared under, and until now it could only say "Smith John" — which is exactly the pair of names
    /// the particle exists to tell apart, since a father and a son on one return share everything else.
    /// The client themselves has been asked for one since intake; the people declared with them were not.
    /// </para>
    /// <para>
    /// NULLABLE and left blank on every row already on file. Nothing is backfilled and nothing is
    /// inferred: the particle is an answer the client gives, and a row declared before the box existed
    /// has none. A blank one simply prints nothing after the name.
    /// </para>
    /// <para>
    /// 16 characters, the cap the client's own <c>Persons.Suffix</c> carries — it is the same box asking
    /// the same question, and the answer is a particle rather than a title.
    /// </para>
    /// </summary>
    public partial class AddRemsAdditionalIndividualSuffix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "REMSAdditionalIndividual",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "REMSAdditionalIndividual");
        }
    }
}
