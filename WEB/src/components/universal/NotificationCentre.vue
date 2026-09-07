<template>
  <!-- grey-8 matches the user menu sitting next to it in the header. -->
  <q-btn flat round dense color="grey-8" icon="o_notifications" aria-label="Notifications">
    <q-badge v-if="unread > 0" floating color="negative" :label="unread > 99 ? '99+' : unread" />
    <q-menu anchor="bottom right" self="top right" @show="loadLatest">
      <q-card style="width: 500px; max-width: 90vw;">
        <q-card-section class="row items-center q-py-sm">
          <div class="text-subtitle1 text-weight-medium">Notifications</div>
          <q-space />
          <q-btn flat dense no-caps size="sm" color="primary" label="Mark all read" :disable="!unread" @click="markAllRead" />
        </q-card-section>
        <q-separator />

        <q-list separator style="max-height: 360px;" class="scroll">
          <q-item v-if="!latest.length" class="text-grey-6">
            <q-item-section>No notifications.</q-item-section>
          </q-item>
          <q-item
            v-for="n in latest"
            :key="n.id"
            v-close-popup
            clickable
            :class="{ 'bg-teal-1': !n.isRead }"
            @click="openNotification(n)"
          >
            <q-item-section avatar>
              <q-icon :name="metaFor(n.type).icon" :color="metaFor(n.type).color" />
            </q-item-section>
            <q-item-section>
              <!-- The title WRAPS. It was `ellipsis` — one line, cut off with a "…" — and a title is
                   the one part of a notification that has to be readable without opening. -->
              <q-item-label class="text-weight-medium notif__title">{{ n.title }}</q-item-label>
              <q-item-label caption lines="2">{{ n.body }}</q-item-label>
              <q-item-label caption>{{ formatDateTime(n.createdOnUtc) }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-list>

        <q-separator />
        <q-card-actions align="center">
          <q-btn v-close-popup flat no-caps color="primary" label="View all" :to="{ name: 'uf_notifications' }" />
        </q-card-actions>
      </q-card>
    </q-menu>
  </q-btn>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from "vue";
import { useRouter } from "vue-router";
import { ufNotificationApi } from "services/api";
import { useDateFormat } from "composables/useDateFormat";
import { useNotificationMeta } from "composables/uf/useNotificationMeta";
import { useNotificationRoute } from "composables/uf/useNotificationRoute";

const router = useRouter();
const { formatDateTime } = useDateFormat();
const { metaFor } = useNotificationMeta();
const { routeForNotification } = useNotificationRoute();

const unread = ref(0);
const latest = ref([]);
let timer = null;

const loadUnread = async () => {
  try {
    const res = await ufNotificationApi.unreadCount();
    unread.value = res?.unread ?? 0;
  } catch { /* ignore */ }
};

const loadLatest = async () => {
  try {
    const res = await ufNotificationApi.list({ page: 1, limit: 10 });
    latest.value = res?.data || [];
  } catch { /* ignore */ }
};

const markAllRead = async () => {
  try {
    await ufNotificationApi.markAllRead();
    unread.value = 0;
    latest.value = latest.value.map((n) => ({ ...n, isRead: true }));
  } catch { /* ignore */ }
};

const openNotification = async (n) => {
  if (!n.isRead) {
    try {
      await ufNotificationApi.markRead(n.id);
      unread.value = Math.max(0, unread.value - 1);
    } catch { /* ignore */ }
  }
  if (n.entityType && n.entityId) {
    router.push(await routeForNotification(n));
  }
};

onMounted(() => {
  loadUnread();
  timer = setInterval(loadUnread, 30000);
});

onBeforeUnmount(() => clearInterval(timer));
</script>

<style scoped>
/* q-item-label truncates by default inside a q-item — the class above only removed Quasar's `ellipsis`,
   and the label still needs telling it may wrap. Words break where a long client or entity name would
   otherwise push the row wider than the menu. */
.notif__title {
  white-space: normal;
  overflow-wrap: anywhere;
}
</style>
