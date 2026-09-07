import { Notify, Dialog, LocalStorage } from "quasar";
import Alert from "dialogs/alert.vue";
import Confirmation from "dialogs/confirmation.vue";
import DeleteConfirmation from "dialogs/delete_confirmation.vue";
import ModuleConfirmation from "dialogs/module_confirmation.vue";
import UpdateConfirmation from "dialogs/update_confirmation.vue";
// import DancingLadyLoader from "pages/DancingLadyLoader.vue"; // Import your custom loader component

export function setLocalStorage (key, value) {
  return LocalStorage.set(key, value);
}

export function getLocalStorage (key) {
  return LocalStorage.getItem(key);
}

export function clearLocalStorage (key) {
  return LocalStorage.remove(key);
}

// export function showLoader () { Loading.show({ spinner: DancingLadyLoader, // Use your custom component
// here backgroundColor: "grey-8", message.

// export function hideLoader () { Loading.hide(); }

export function notifySuccess (data) {
  Notify.create({
    message: data?.message,
    caption: data?.caption,
    type: "positive",
    icon: "o_check_circle",
    multiLine: true,
    html: true,
    position: "bottom",
    actions: data?.actions
  });
}

export function notifyWarning (data) {
  Notify.create({
    message: data?.message,
    caption: data?.caption,
    icon: "o_warning",
    type: "warning",
    multiLine: true,
    html: true,
    position: "bottom",
    actions: data?.actions
  });
}

export function notifyError (data) {
  Notify.create({
    message: data?.message,
    caption: data?.caption,
    timeout: data?.timeout,
    icon: "o_error",
    type: "negative",
    multiLine: true,
    html: true,
    position: "bottom",
    actions: data?.actions
  });
}

export function notifyInfo (data) {
  Notify.create({
    message: data?.message,
    caption: data?.caption,
    icon: "o_info",
    type: "info",
    multiLine: true,
    html: true,
    position: "bottom",
    actions: data?.actions
  });
}

export function zwAlert (options, okCallback, cancelCallback, dismissCallback) {
  Dialog.create({
    component: Alert,
    componentProps: options
  })
    .onOk(() => {
      if (okCallback) {
        okCallback();
      }
    })
    .onCancel(() => {
      if (cancelCallback) {
        cancelCallback();
      }
    })
    .onDismiss(() => {
      if (dismissCallback) {
        dismissCallback();
      }
    });
}

export function zwConfirm (options, okCallback, cancelCallback, dismissCallback) {
  Dialog.create({
    component: Confirmation,
    componentProps: options
  })
    .onOk(() => {
      if (okCallback) {
        okCallback();
      }
    })
    .onCancel(() => {
      if (cancelCallback) {
        cancelCallback();
      }
    })
    .onDismiss(() => {
      if (dismissCallback) {
        dismissCallback();
      }
    });
}

export function zwConfirmDelete (options, okCallback, cancelCallback, dismissCallback) {
  Dialog.create({
    component: DeleteConfirmation,
    componentProps: options
  })
    .onOk(() => {
      if (okCallback) {
        okCallback();
      }
    })
    .onCancel(() => {
      if (cancelCallback) {
        cancelCallback();
      }
    })
    .onDismiss(() => {
      if (dismissCallback) {
        dismissCallback();
      }
    });
}

export function zwConfirmModule (options, okCallback, cancelCallback, dismissCallback) {
  Dialog.create({
    component: ModuleConfirmation,
    componentProps: options
  })
    .onOk(() => {
      if (okCallback) {
        okCallback();
      }
    })
    .onCancel(() => {
      if (cancelCallback) {
        cancelCallback();
      }
    })
    .onDismiss(() => {
      if (dismissCallback) {
        dismissCallback();
      }
    });
}

export function zwConfirmLeave (options, okCallback, cancelCallback, dismissCallback) {
  Dialog.create({
    component: UpdateConfirmation,
    componentProps: options
  })
    .onOk(() => {
      if (okCallback) {
        okCallback();
      }
    })
    .onCancel(() => {
      if (cancelCallback) {
        cancelCallback();
      }
    })
    .onDismiss(() => {
      if (dismissCallback) {
        dismissCallback();
      }
    });
}
