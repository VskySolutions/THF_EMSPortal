<template>
  <!-- A conversation read the way a conversation is read: oldest at the top, newest at the bottom, the
       thread pinned to the latest message, and a box to type in that is always there. -->
  <div class="uf-chat" :style="{ height }">
    <!-- Only where there is something to search. -->
    <div v-if="searchable" class="uf-chat__head">
      <q-input
        v-model="search"
        dense outlined clearable debounce="300"
        placeholder="Search this conversation"
        class="uf-chat__search"
        :loading="loading"
        @update:model-value="onSearch"
      >
        <template #prepend><q-icon name="o_search" size="18px" /></template>
      </q-input>
      <div v-if="search" class="uf-chat__head-count">
        {{ messages.length }} {{ messages.length === 1 ? "match" : "matches" }}
      </div>
    </div>

    <!-- The thread. Owns its own scroll so the composer below it stays put while it moves. -->
    <div ref="scrollerRef" class="uf-chat__thread" @scroll="onScroll">
      <q-inner-loading :showing="loading && !messages.length" />

      <div v-if="!loading && !messages.length" class="uf-chat__empty">
        <q-icon :name="search ? 'o_search_off' : 'o_forum'" size="34px" />
        <div class="q-mt-sm">{{ search ? "No messages match that." : "No messages yet." }}</div>
        <div v-if="!search" class="text-caption">Start the conversation below — type @ to bring somebody in.</div>
      </div>

      <template v-for="item in timeline" :key="item.key">
        <!-- Today / Yesterday / the date. What turns a run of clock times into a sequence of days. -->
        <div v-if="item.kind === 'day'" class="uf-chat__day"><span>{{ item.label }}</span></div>

        <div
          v-else
          class="uf-chat__row"
          :class="{ 'uf-chat__row--mine': item.mine, 'uf-chat__row--tight': !item.leads }"
        >
          <!-- Only on the first message of a run, and never on your own: your own messages are the ones on
               the right, which says whose they are without a picture repeating it. -->
          <q-avatar
            v-if="!item.mine && item.leads" size="30px"
            :style="avatarStyle(item.message.authorId)" class="uf-chat__avatar"
          >
            {{ initials(item.message.authorName) }}
          </q-avatar>
          <div v-else-if="!item.mine" class="uf-chat__avatar-gap" />

          <div class="uf-chat__bubble" :class="{ 'uf-chat__bubble--mine': item.mine }">
            <div
              v-if="!item.mine && item.leads" class="uf-chat__author"
              :style="{ color: authorColor(item.message.authorId) }"
            >
              {{ item.message.authorName || "Unknown" }}
            </div>

            <div v-if="editingId === item.message.id" class="uf-chat__edit">
              <q-editor
                v-model="editDraft" min-height="4rem" :toolbar="toolbar"
                content-class="uf-chat__editor-content"
              />
              <div class="row justify-end q-gutter-xs q-mt-xs">
                <q-btn dense no-caps flat size="sm" label="Cancel" @click="editingId = null" />
                <q-btn
                  dense no-caps unelevated size="sm" color="primary" label="Save"
                  :disable="!hasContent(editDraft)" @click="saveEdit(item.message)"
                />
              </div>
            </div>

            <template v-else>
              <!-- eslint-disable-next-line vue/no-v-html -->
              <div class="uf-chat__body" v-html="renderBody(item.message.body)" />
              <div class="uf-chat__meta">
                <span v-if="item.message.isEdited" class="uf-chat__edited">edited</span>
                {{ formatTime(item.message.createdOnUtc) }}
              </div>
            </template>

            <!-- Held inside the bubble and revealed on hover, so a thread at rest is messages rather than
                 messages with two buttons beside each of them. -->
            <q-btn
              v-if="editingId !== item.message.id && (canEdit(item.message) || canDelete(item.message))"
              flat round dense size="xs" icon="o_more_vert" class="uf-chat__actions"
            >
              <q-menu auto-close anchor="bottom right" self="top right">
                <q-list dense style="min-width: 130px;">
                  <q-item v-if="canEdit(item.message)" clickable @click="startEdit(item.message)">
                    <q-item-section avatar><q-icon name="o_edit" size="18px" /></q-item-section>
                    <q-item-section>Edit</q-item-section>
                  </q-item>
                  <q-item v-if="canDelete(item.message)" clickable class="text-negative" @click="remove(item.message)">
                    <q-item-section avatar><q-icon name="o_delete" size="18px" /></q-item-section>
                    <q-item-section>Delete</q-item-section>
                  </q-item>
                </q-list>
              </q-menu>
            </q-btn>
          </div>
        </div>
      </template>
    </div>

    <!-- Offered only once the reader has scrolled away from the bottom: a button that jumps you to where
         you already are is a button in the way. -->
    <q-btn
      v-show="!atBottom && messages.length" round unelevated color="primary" size="sm"
      icon="o_keyboard_arrow_down" class="uf-chat__jump" @click="scrollToBottom(true)"
    />

    <!-- The composer, always present. -->
    <div class="uf-chat__composer" :class="{ 'uf-chat__composer--focused': composerFocused }">
      <q-editor
        ref="editorRef"
        v-model="draft"
        flat
        min-height="2.25rem"
        max-height="9rem"
        :toolbar="formatting ? toolbar : []"
        placeholder="Write a message…  @ to mention someone"
        content-class="uf-chat__editor-content"
        class="uf-chat__editor"
        :class="{ 'uf-chat__editor--formatting': formatting }"
        @focusin="onComposerFocus"
        @focusout="composerFocused = false"
        @keydown="onComposerKeydown"
      />
      <div class="uf-chat__composer-actions">
        <q-btn
          flat round dense size="sm" icon="o_text_format"
          :color="formatting ? 'primary' : 'grey-7'" @click="formatting = !formatting"
        >
          <q-tooltip>{{ formatting ? "Hide formatting" : "Formatting" }}</q-tooltip>
        </q-btn>
        <q-btn
          round unelevated dense color="primary" icon="o_send"
          :loading="posting" :disable="!hasContent(draft)" @click="post"
        >
          <q-tooltip>Send · Enter</q-tooltip>
        </q-btn>
      </div>

      <!-- @mention autocomplete. -->
      <q-menu
        v-model="mentionOpen"
        no-focus no-parent-event no-refocus
        anchor="top left" self="bottom left" :offset="[0, 6]"
      >
        <q-list dense style="min-width: 230px; max-height: 240px;" class="scroll">
          <q-item v-if="!candidates.length" dense>
            <q-item-section class="text-grey-6">No matches</q-item-section>
          </q-item>
          <q-item
            v-for="(c, i) in candidates" :key="c.userId"
            v-close-popup clickable dense
            :active="i === mentionIndex" active-class="bg-blue-1"
            @click="pickMention(c)"
          >
            <q-item-section avatar>
              <q-avatar size="26px" :style="avatarStyle(c.userId)">{{ initials(c.name) }}</q-avatar>
            </q-item-section>
            <q-item-section>
              <q-item-label>{{ c.name }}</q-item-label>
              <q-item-label v-if="c.email" caption>{{ c.email }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-list>
      </q-menu>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, nextTick, onMounted, onBeforeUnmount } from "vue";
import { ufConversationApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useAuthStore } from "stores/auth";
import { escapeHtml, sanitizeHtml, isHtml, hasRichTextContent as hasContent } from "utils/richText";
// Token markup and id extraction are shared with AppRichTextField's CKEditor mentions — one definition,
// so a mention typed in either editor resolves in both.
import { mentionTokenHtml, extractMentionIds, fetchMentionCandidates } from "composables/useMentions";

const props = defineProps({
  entityType: { type: Number, required: true },
  entityId: { type: String, required: true },
  // How tall the whole panel is.
  height: { type: String, default: "440px" }
});

const notify = useNotify();
const { confirm } = useConfirm();
const { formatDate, formatTime } = useDateFormat();
const { has } = usePermissions();
const auth = useAuthStore();
const currentUserId = auth.user?.userId || null;
const isAdmin = has(Permissions.SettingsManage);

const toolbar = [
  ["bold", "italic", "underline", "strike"],
  ["unordered", "ordered"],
  ["link"],
  ["removeFormat"]
];

const messages = ref([]);
const loading = ref(false);
const search = ref("");
const searchable = computed(() => !!search.value || messages.value.length > 1);

const draft = ref("");
const posting = ref(false);
const formatting = ref(false);
const composerFocused = ref(false);

// @mention autocomplete state
const editorRef = ref(null);
const mentionOpen = ref(false);
const candidates = ref([]);
const mentionIndex = ref(0);
let mentionTimer = null;
let mentionContentEl = null;
let mentionRange = null; // the range covering the typed "@query"

const editingId = ref(null);
const editDraft = ref("");

const scrollerRef = ref(null);
const atBottom = ref(true);

// ---- data ----
const load = async ({ keepPosition = false } = {}) => {
  loading.value = true;
  try {
    const res = await ufConversationApi.list({
      entityType: props.entityType,
      entityId: props.entityId,
      search: search.value || undefined,
      page: 1,
      limit: 100
    });
    const rows = res?.data || [];
    // OLDEST first, regardless of server ordering — a conversation is read downwards, and a reply
    // sitting above the message it answers was the single thing that made the old panel hard to follow.
    messages.value = rows.sort((a, b) => new Date(a.createdOnUtc) - new Date(b.createdOnUtc));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
    if (!keepPosition) await scrollToBottom();
  }
};

const onSearch = () => load({ keepPosition: true });

// ---- the timeline: day headings, and who said what next to each other ----
// Consecutive messages from one author inside this window are one turn of speech: the avatar and the name
// are said once at the top of the run rather than over every line of it.
const GROUP_WINDOW_MS = 5 * 60 * 1000;

const dayLabel = (value) => {
  const day = formatDate(value, "");
  if (!day) return "";
  const now = Date.now();
  if (day === formatDate(new Date(now).toISOString(), "")) return "Today";
  if (day === formatDate(new Date(now - 86400000).toISOString(), "")) return "Yesterday";
  return day;
};

const timeline = computed(() => {
  const out = [];
  let lastDay = null;
  let previous = null;
  for (const message of messages.value) {
    const day = formatDate(message.createdOnUtc, "");
    if (day !== lastDay) {
      out.push({ kind: "day", key: `day-${day}`, label: dayLabel(message.createdOnUtc) });
      lastDay = day;
      previous = null; // a new day always opens a new run, whatever the clock says
    }
    const sameAuthor = previous && previous.authorId === message.authorId;
    const close = previous &&
      (new Date(message.createdOnUtc) - new Date(previous.createdOnUtc)) < GROUP_WINDOW_MS;
    out.push({
      kind: "message",
      key: message.id,
      message,
      mine: !!message.authorId && message.authorId === currentUserId,
      leads: !(sameAuthor && close)
    });
    previous = message;
  }
  return out;
});

// ---- scrolling ----
const scrollToBottom = async (smooth = false) => {
  await nextTick();
  const el = scrollerRef.value;
  if (!el) return;
  el.scrollTo({ top: el.scrollHeight, behavior: smooth ? "smooth" : "auto" });
  atBottom.value = true;
};

// Within a few pixels counts as the bottom: a thread sitting one pixel short of it should not be
// offering to take the reader somewhere they already are.
const onScroll = () => {
  const el = scrollerRef.value;
  if (!el) return;
  atBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
};

// ---- @mention autocomplete inside the contenteditable editor ----
const editorContentEl = () =>
  editorRef.value?.getContentEl?.() || editorRef.value?.$el?.querySelector(".q-editor__content") || null;

const onComposerFocus = () => {
  composerFocused.value = true;
  wireMentionListener();
};

const wireMentionListener = () => {
  const el = editorContentEl();
  if (el && el !== mentionContentEl) {
    unwireMentionListener();
    mentionContentEl = el;
    el.addEventListener("keyup", onEditorKeyup);
  }
};

const unwireMentionListener = () => {
  mentionContentEl?.removeEventListener("keyup", onEditorKeyup);
  mentionContentEl = null;
};

const onEditorKeyup = (e) => {
  // The keys that DRIVE the menu are handled on keydown; re-reading the caret after one of them would
  // close the menu the moment the reader tried to walk down it.
  if (mentionOpen.value && ["ArrowUp", "ArrowDown", "Enter", "Escape", "Tab"].includes(e.key)) return;

  const sel = window.getSelection();
  if (!sel || !sel.rangeCount) { mentionOpen.value = false; return; }
  const range = sel.getRangeAt(0);
  const node = range.startContainer;
  if (node.nodeType !== Node.TEXT_NODE) { mentionOpen.value = false; return; }

  const textBefore = node.textContent.slice(0, range.startOffset);
  const match = /@(\w*)$/.exec(textBefore);
  if (!match) { mentionOpen.value = false; return; }

  // Remember the exact span of the "@query" so we can replace it on selection.
  mentionRange = document.createRange();
  mentionRange.setStart(node, range.startOffset - match[0].length);
  mentionRange.setEnd(node, range.startOffset);

  clearTimeout(mentionTimer);
  mentionTimer = setTimeout(() => fetchCandidates(match[1]), 200);
};

const fetchCandidates = async (term) => {
  try {
    candidates.value = await fetchMentionCandidates(term);
    mentionIndex.value = 0;
    mentionOpen.value = true;
  } catch {
    candidates.value = [];
  }
};

// Enter sends and Shift+Enter breaks the line, which is what every chat box does and therefore what fingers
// already expect.
const onComposerKeydown = (e) => {
  if (mentionOpen.value) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      mentionIndex.value = (mentionIndex.value + 1) % Math.max(candidates.value.length, 1);
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      const n = Math.max(candidates.value.length, 1);
      mentionIndex.value = (mentionIndex.value - 1 + n) % n;
      return;
    }
    if (e.key === "Enter" || e.key === "Tab") {
      e.preventDefault();
      const picked = candidates.value[mentionIndex.value];
      if (picked) pickMention(picked);
      return;
    }
    if (e.key === "Escape") {
      e.preventDefault();
      mentionOpen.value = false;
      return;
    }
  }
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    if (hasContent(draft.value) && !posting.value) post();
  }
};

const pickMention = (c) => {
  editorRef.value?.focus?.();
  // Reselect the "@query" range and replace it with a non-editable mention token.
  if (mentionRange) {
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(mentionRange);
  }
  const html = `${mentionTokenHtml(c)}&nbsp;`;
  editorRef.value?.runCmd?.("insertHTML", html);
  mentionRange = null;
  mentionOpen.value = false;
};

// ---- post / edit / delete ----
const post = async () => {
  if (!hasContent(draft.value)) return;
  posting.value = true;
  try {
    await ufConversationApi.create({
      entityType: props.entityType,
      entityId: props.entityId,
      body: draft.value,
      mentionedUserIds: extractMentionIds(draft.value)
    });
    // No toast. The message appearing at the foot of the thread IS the confirmation, and a chat that
    // announces every line you send is a chat talking over itself.
    draft.value = "";
    mentionOpen.value = false;
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    posting.value = false;
  }
};

const canEdit = (message) => message.authorId && message.authorId === currentUserId;
const canDelete = (message) => canEdit(message) || isAdmin;

const startEdit = (message) => {
  editingId.value = message.id;
  editDraft.value = message.body;
};

const saveEdit = async (message) => {
  try {
    await ufConversationApi.update(message.id, { body: editDraft.value, mentionedUserIds: extractMentionIds(editDraft.value) });
    editingId.value = null;
    // Kept where the reader was: correcting an old message should not throw the thread to the bottom.
    await load({ keepPosition: true });
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const remove = async (message) => {
  const ok = await confirm({
    title: "Delete message",
    message: "Delete this message? This cannot be undone.",
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await ufConversationApi.remove(message.id);
    await load({ keepPosition: true });
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- rendering ----
// Sanitizing/escaping lives in utils/richText so the conversation and the description editors share one
// allowlist.

// New messages are q-editor HTML; ones written before the rich editor are plain text with @[Name](id) tokens.
const renderBody = (body) => {
  if (isHtml(body)) {
    return sanitizeHtml(body);
  }
  return escapeHtml(body).replace(
    /@\[([^\]]+)\]\([0-9a-fA-F-]{36}\)/g,
    (_m, name) => `<span class="uf-mention">@${escapeHtml(name)}</span>`
  );
};

const initials = (name) => (name || "?").split(" ").map((p) => p[0]).slice(0, 2).join("").toUpperCase();

// One colour per person, derived from their id.
const AVATAR_COLORS = [
  "#1565c0", "#00838f", "#2e7d32", "#6a1b9a", "#c62828",
  "#ad1457", "#4527a0", "#00695c", "#ef6c00", "#37474f"
];
const authorColor = (id) => {
  const key = String(id || "");
  let hash = 0;
  for (let i = 0; i < key.length; i += 1) hash = (hash * 31 + key.charCodeAt(i)) >>> 0;
  return AVATAR_COLORS[hash % AVATAR_COLORS.length];
};
const avatarStyle = (id) => ({ backgroundColor: authorColor(id), color: "#fff", fontSize: "12px" });

onMounted(load);
onBeforeUnmount(() => {
  clearTimeout(mentionTimer);
  unwireMentionListener();
});
defineExpose({ load });
</script>

<style scoped>
.uf-chat {
  display: flex;
  flex-direction: column;
  /* min-height:0 on a flex child is what lets the thread below actually scroll instead of growing the
     panel past its own height. */
  min-height: 0;
}

/* ---- header ---- */
.uf-chat__head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding-bottom: 8px;
}
.uf-chat__search { flex: 1 1 auto; max-width: 280px; }
.uf-chat__head-count { font-size: 12px; color: var(--ink-500, #5a6675); }

/* ---- the thread ---- */
.uf-chat__thread {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  padding: 10px 4px 6px;
  background: #f5f7f9;
  border: 1px solid #e2e7ee;
  border-radius: 10px;
}
.uf-chat__empty {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: #98a2b0;
  text-align: center;
}

/* Today / Yesterday / the date, centred on the thread. */
.uf-chat__day {
  display: flex;
  align-items: center;
  justify-content: center;
  margin: 10px 0 12px;
}
.uf-chat__day span {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #6b7684;
  background: #e7ebf0;
  border-radius: 999px;
  padding: 3px 12px;
}

/* ---- a message ---- */
.uf-chat__row {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 0 8px;
  margin-bottom: 8px;
}
/* Inside one person's run the lines sit close, so the run reads as one turn of speech. */
.uf-chat__row--tight { margin-bottom: 2px; }
.uf-chat__row--mine { justify-content: flex-end; }
.uf-chat__avatar { font-weight: 600; flex: 0 0 auto; }
/* Keeps the later lines of a run aligned with the first, which is the one carrying the avatar. */
.uf-chat__avatar-gap { flex: 0 0 30px; }

.uf-chat__bubble {
  position: relative;
  max-width: min(76%, 520px);
  padding: 7px 11px 5px;
  background: #fff;
  border: 1px solid #e4e9f0;
  border-radius: 12px 12px 12px 3px;
  box-shadow: 0 1px 1px rgba(16, 24, 40, 0.04);
}
.uf-chat__row--tight .uf-chat__bubble { border-radius: 12px; }
.uf-chat__bubble--mine {
  /* The tint the rest of the app uses for a selected row, so "mine" reads as this app rather than as
     somebody else's chat client. */
  background: #e3f1fd;
  border-color: #cfe4f7;
  border-radius: 12px 12px 3px 12px;
}
.uf-chat__row--tight .uf-chat__bubble--mine { border-radius: 12px; }

.uf-chat__author {
  font-size: 12px;
  font-weight: 600;
  margin-bottom: 2px;
}
.uf-chat__body {
  font-size: 13.5px;
  line-height: 1.45;
  color: #1f2937;
  white-space: normal;
  word-break: break-word;
}
.uf-chat__body :deep(p) { margin: 0 0 4px; }
.uf-chat__body :deep(p:last-child) { margin-bottom: 0; }
.uf-chat__body :deep(ul), .uf-chat__body :deep(ol) { margin: 2px 0 4px; padding-left: 20px; }
.uf-chat__body :deep(.uf-mention) {
  color: #1976d2;
  font-weight: 500;
  background: #e3f2fd;
  border-radius: 4px;
  padding: 0 3px;
}
/* On the blue bubble the mention's own blue tint disappears into it. */
.uf-chat__bubble--mine .uf-chat__body :deep(.uf-mention) { background: #fff; }

/* The time sits with the message rather than above it, right-aligned, out of the way of the words. */
.uf-chat__meta {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
  margin-top: 2px;
  font-size: 10.5px;
  color: #8a94a3;
  white-space: nowrap;
}
.uf-chat__edited { font-style: italic; }

.uf-chat__actions {
  position: absolute;
  top: 2px;
  right: 2px;
  opacity: 0;
  transition: opacity 0.12s;
  color: #8a94a3;
}
.uf-chat__bubble:hover .uf-chat__actions { opacity: 1; }
.uf-chat__edit { min-width: 260px; }

/* ---- jump to the newest ---- */
.uf-chat__jump {
  position: absolute;
  right: 18px;
  bottom: 78px;
  z-index: 1;
  box-shadow: 0 2px 8px rgba(16, 24, 40, 0.2);
}

/* ---- composer ---- */
.uf-chat__composer {
  position: relative;
  display: flex;
  align-items: flex-end;
  gap: 6px;
  margin-top: 8px;
  padding: 4px 4px 4px 6px;
  background: #fff;
  border: 1px solid #d6dde6;
  border-radius: 12px;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.uf-chat__composer--focused {
  border-color: var(--q-primary);
  box-shadow: 0 0 0 2px rgba(25, 118, 210, 0.12);
}
.uf-chat__editor {
  flex: 1 1 auto;
  min-width: 0;
  border: none;
}
/* The editor's own chrome belongs to the composer around it now — one border, not two. */
.uf-chat__editor :deep(.q-editor__toolbar) {
  border: none;
  border-bottom: 1px solid #eef1f5;
  padding: 0;
}
/* An empty `toolbar` array still renders the bar, so the rule above it stayed on screen with nothing
   over it. Hidden outright until the formatting button asks for it. */
.uf-chat__editor:not(.uf-chat__editor--formatting) :deep(.q-editor__toolbar) { display: none; }
.uf-chat__editor :deep(.q-editor__content) {
  padding: 6px 4px;
  font-size: 13.5px;
  line-height: 1.45;
  overflow-y: auto;
}
.uf-chat__composer-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  padding-bottom: 2px;
}
</style>
