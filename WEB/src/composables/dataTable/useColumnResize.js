import { ref, onBeforeUnmount } from "vue";

const MIN_COLUMN_WIDTH = 80;
const DEFAULT_COLUMN_WIDTH = 120;

export default function useColumnResize ({
  columns,
  resizeWidths,
  saveResizableWidthState
}) {
  const isResizing = ref(false);

  let resizing = null;

  // The click a browser fires after the mouseup that ends a drag lands on the header cell, where QTh reads
  // it as a sort. It is flagged for the length of that one dispatch, so the table can swallow it.
  let clickPending = false;

  const ensureWidths = () => {
    if (!resizeWidths.value) {
      resizeWidths.value = {};
    }

    if (Object.keys(resizeWidths.value).length === 0) {
      const widths = {};

      for (const col of columns.value) {
        widths[col.name] = DEFAULT_COLUMN_WIDTH;
      }

      resizeWidths.value = widths;
    }

    return resizeWidths.value;
  };

  const startResize = (event, columnName) => {
    if (!columnName) {
      console.error("Missing columnName in startResize");
      return;
    }

    const widths = ensureWidths();

    isResizing.value = true;

    resizing = {
      startX: event.pageX,
      startWidth: widths[columnName] || DEFAULT_COLUMN_WIDTH,
      columnName
    };

    document.addEventListener("mousemove", handleResize);
    document.addEventListener("mouseup", stopResize);
  };

  const handleResize = (event) => {
    if (!resizing) return;

    const deltaX = event.pageX - resizing.startX;

    const width = Math.max(
      MIN_COLUMN_WIDTH,
      resizing.startWidth + deltaX
    );

    resizeWidths.value = {
      ...resizeWidths.value,
      [resizing.columnName]: width
    };
  };

  const stopResize = () => {
    if (!resizing) return;

    document.removeEventListener("mousemove", handleResize);
    document.removeEventListener("mouseup", stopResize);

    isResizing.value = false;

    saveResizableWidthState({
      ...resizeWidths.value
    });

    resizing = null;

    clickPending = true;
    setTimeout(() => { clickPending = false; }, 0);
  };

  const isResizeClick = () => clickPending;

  const resetColumnsWidth = () => {
    const widths = {};

    for (const col of columns.value) {
      widths[col.name] = DEFAULT_COLUMN_WIDTH;
    }

    resizeWidths.value = widths;

    saveResizableWidthState({
      ...widths
    });
  };

  onBeforeUnmount(() => {
    document.removeEventListener("mousemove", handleResize);
    document.removeEventListener("mouseup", stopResize);
  });

  ensureWidths();

  return {
    startResize,
    stopResize,
    resetColumnsWidth,
    isResizing,
    isResizeClick
  };
}
