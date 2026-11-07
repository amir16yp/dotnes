(function () {
  const vscode = acquireVsCodeApi();
  const COLORS = ["#0d0d0d", "#555555", "#aaaaaa", "#f0f0f0"];

  const sheet = document.getElementById("sheet");
  const detail = document.getElementById("detail");
  const colorSelect = document.getElementById("color");
  const zoomInput = document.getElementById("zoom");
  const status = document.getElementById("status");
  const palette = document.getElementById("palette");
  const saveBtn = document.getElementById("save");

  const sheetCtx = sheet.getContext("2d");
  const detailCtx = detail.getContext("2d");

  let tiles = [];
  let selected = 0;
  let zoom = 4;
  let cols = 16;
  let painting = false;

  for (let i = 0; i < 4; i++) {
    const sw = document.createElement("button");
    sw.type = "button";
    sw.className = "swatch" + (i === 1 ? " active" : "");
    sw.dataset.c = String(i);
    sw.title = "Color " + i;
    sw.addEventListener("click", () => {
      colorSelect.value = String(i);
      document.querySelectorAll(".swatch").forEach((el) => el.classList.remove("active"));
      sw.classList.add("active");
    });
    palette.appendChild(sw);
  }

  colorSelect.addEventListener("change", () => {
    document.querySelectorAll(".swatch").forEach((el) => {
      el.classList.toggle("active", el.dataset.c === colorSelect.value);
    });
  });

  zoomInput.addEventListener("input", () => {
    zoom = Number(zoomInput.value) || 4;
    drawSheet();
  });

  saveBtn.addEventListener("click", () => vscode.postMessage({ type: "save" }));

  function currentColor() {
    return Number(colorSelect.value) & 3;
  }

  function drawSheet() {
    const rows = Math.max(1, Math.ceil(tiles.length / cols));
    const tilePx = 8 * zoom;
    sheet.width = cols * tilePx;
    sheet.height = rows * tilePx;
    sheetCtx.fillStyle = "#111318";
    sheetCtx.fillRect(0, 0, sheet.width, sheet.height);

    for (let t = 0; t < tiles.length; t++) {
      const tx = (t % cols) * tilePx;
      const ty = Math.floor(t / cols) * tilePx;
      drawTile(sheetCtx, tiles[t], tx, ty, zoom);
      if (t === selected) {
        sheetCtx.strokeStyle = "#5b9fd4";
        sheetCtx.lineWidth = 2;
        sheetCtx.strokeRect(tx + 1, ty + 1, tilePx - 2, tilePx - 2);
      }
    }
  }

  function drawDetail() {
    detailCtx.fillStyle = "#111318";
    detailCtx.fillRect(0, 0, detail.width, detail.height);
    if (!tiles[selected]) return;
    const scale = detail.width / 8;
    drawTile(detailCtx, tiles[selected], 0, 0, scale);
    detailCtx.strokeStyle = "#333842";
    detailCtx.lineWidth = 1;
    for (let i = 0; i <= 8; i++) {
      detailCtx.beginPath();
      detailCtx.moveTo(i * scale, 0);
      detailCtx.lineTo(i * scale, detail.height);
      detailCtx.stroke();
      detailCtx.beginPath();
      detailCtx.moveTo(0, i * scale);
      detailCtx.lineTo(detail.width, i * scale);
      detailCtx.stroke();
    }
    status.textContent = "Tile $" + selected.toString(16).toUpperCase().padStart(2, "0") + " (" + selected + ")";
  }

  function drawTile(ctx, tile, ox, oy, scale) {
    if (!tile || tile.length !== 64) return;
    for (let y = 0; y < 8; y++) {
      for (let x = 0; x < 8; x++) {
        const v = tile[y * 8 + x] & 3;
        ctx.fillStyle = COLORS[v];
        ctx.fillRect(ox + x * scale, oy + y * scale, scale, scale);
      }
    }
  }

  function paintDetail(ev) {
    if (!tiles[selected]) return;
    const rect = detail.getBoundingClientRect();
    const scale = detail.width / 8;
    const x = Math.floor(((ev.clientX - rect.left) / rect.width) * 8);
    const y = Math.floor(((ev.clientY - rect.top) / rect.height) * 8);
    if (x < 0 || y < 0 || x > 7 || y > 7) return;
    const pixelIndex = y * 8 + x;
    const color = currentColor();
    if (tiles[selected][pixelIndex] === color) return;
    tiles[selected][pixelIndex] = color;
    vscode.postMessage({ type: "paint", tileIndex: selected, pixelIndex, color });
    drawDetail();
    drawSheet();
  }

  detail.addEventListener("mousedown", (ev) => {
    painting = true;
    paintDetail(ev);
  });
  detail.addEventListener("mousemove", (ev) => {
    if (painting) paintDetail(ev);
  });
  window.addEventListener("mouseup", () => {
    painting = false;
  });

  function tileIndexAt(ev, canvas) {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    const x = (ev.clientX - rect.left) * scaleX;
    const y = (ev.clientY - rect.top) * scaleY;
    const tilePx = 8 * zoom;
    const col = Math.floor(x / tilePx);
    const row = Math.floor(y / tilePx);
    const idx = row * cols + col;
    return idx >= 0 && idx < tiles.length ? idx : -1;
  }

  sheet.addEventListener("click", (ev) => {
    const idx = tileIndexAt(ev, sheet);
    if (idx >= 0) {
      selected = idx;
      drawSheet();
      drawDetail();
    }
  });

  const menu = document.getElementById("ctx");
  function hideMenu() {
    menu.classList.add("hidden");
  }

  sheet.addEventListener("contextmenu", (ev) => {
    ev.preventDefault();
    const idx = tileIndexAt(ev, sheet);
    if (idx < 0) return;
    selected = idx;
    drawSheet();
    drawDetail();
    menu.style.left = ev.clientX + "px";
    menu.style.top = ev.clientY + "px";
    menu.classList.remove("hidden");
  });

  detail.addEventListener("contextmenu", (ev) => {
    ev.preventDefault();
    if (!tiles[selected]) return;
    menu.style.left = ev.clientX + "px";
    menu.style.top = ev.clientY + "px";
    menu.classList.remove("hidden");
  });

  menu.addEventListener("click", (ev) => {
    const btn = ev.target.closest("[data-cmd]");
    if (!btn) return;
    hideMenu();
    if (btn.dataset.cmd === "findReferences") {
      vscode.postMessage({ type: "findReferences", tileIndex: selected });
    }
  });

  window.addEventListener("click", hideMenu);
  window.addEventListener("blur", hideMenu);
  window.addEventListener("keydown", (ev) => {
    if (ev.key === "Escape") hideMenu();
  });

  window.addEventListener("message", (event) => {
    const msg = event.data;
    if (msg.type === "load") {
      tiles = (msg.tiles || []).map((t) => Array.from(t));
      selected = 0;
      drawSheet();
      drawDetail();
    }
  });

  vscode.postMessage({ type: "ready" });
})();
