import { Dialog } from "quasar";

// Standardized confirmation dialog (UI Development Standards §1.7).
export function useConfirm () {
  const confirm = ({
    title = "Please confirm",
    message = "Are you sure you want to continue?",
    confirmLabel = "Confirm",
    cancelLabel = "Cancel",
    type = "primary"
  } = {}) =>
    new Promise((resolve) => {
      let settled = false;
      const done = (value) => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      Dialog.create({
        title,
        message,
        ok: {
          label: confirmLabel,
          unelevated: true,
          noCaps: true,
          color: type === "danger" ? "negative" : "primary"
        },
        cancel: { label: cancelLabel, flat: true, noCaps: true, color: "grey-8" },
        persistent: true
      })
        .onOk(() => done(true))
        .onCancel(() => done(false))
        .onDismiss(() => done(false));
    });

  return { confirm };
}
