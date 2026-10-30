let viewMode = "schematic";

export function renderSchematic(sketch, volts, layout, mode) {
  viewMode = mode || "schematic";
  return drawSketch(sketch, volts, layout);
}

export function partRole(part) {
      const text = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (/supply|battery|vsource|voltage/.test(text)) return "Power";
      if (/sensor|temperature|thermistor|lm35|tmp36|\bntc\b/.test(text) && !/esp32|mcu|arduino|stm32|controller|\becu\b/.test(text)) return "Sensor";
      if (/(^|[^a-z])(switch|button)([^a-z]|$)/.test(text) && !/mosfet|transistor/.test(text)) return "Input";
      if (/esp32|mcu|arduino|stm32|rp2040|\becu\b|controller/.test(text)) return "Controller";
      if (/mosfet|nmos|transistor|\bnpn\b|\bpnp\b|\bbjt\b|h-bridge|hbridge|tb6612|drv8833/.test(text)) return "Driver";
      if (/fan|motor|pump|solenoid|relay/.test(text) || (/\bled\b/.test(text) && !/resistor/.test(text))) return "Load";
      if (/diode|flyback/.test(text) && !/\bled\b/.test(text)) return "Protection";
      return "";
    }

    function roleTone(role) {
      if (role === "Sensor") return "var(--analog)";
      if (role === "Controller") return "var(--signal)";
      if (role === "Driver") return "var(--bus)";
      if (role === "Load") return "var(--heat)";
      if (role === "Protection") return "var(--probe)";
      if (role === "Power") return "var(--power)";
      return "var(--muted)";
    }

export function circuitRoles(sketch) {
      const roles = [];
      const add = name => { if (name && !roles.includes(name)) roles.push(name); };
      (sketch && sketch.parts || []).forEach(part => add(partRole(part)));
      (sketch && sketch.wires || []).forEach(wire => {
        const text = `${wire.from || ""} ${wire.to || ""}`;
        if (/SDA/i.test(text) && /SCL/i.test(text)) add("Communication");
        if (/MOSI/i.test(text) && /MISO/i.test(text)) add("Communication");
      });
      return roles;
    }

    
export function shownSummary(summary, volts) {
      if (volts == null || volts === undefined || !summary) return summary || "";
      const shown = Number(volts).toFixed(2);
      return summary.replace(/(midpoint|output|vout)([\s\S]{0,40}?)~?\d+(?:\.\d+)?\s*V/i, (_, word, gap) => `${word}${gap}${shown} V`);
    }

    function drawSketch(sketch, volts, layout) {
      const parts = sketch.parts || [];
      if (parts.length === 0 && sketch.diagram) {
        return `<pre class="diagram">${esc(sketch.diagram)}</pre>`;
      }
      if (viewMode === "logic") return drawLogic(sketch);
      if (layout && layout.parts && layout.parts.length) return drawLayout(sketch, layout, volts);
      return drawBlocks(sketch, volts);
    }

    function drawLayout(sketch, layout, volts) {
      const byId = {};
      (sketch.parts || []).forEach(part => { byId[String(part.id).toLowerCase()] = part; });
      const marks = [];
      const wires = [];
      const ink = [];
      const symbols = [];
      const captions = [];
      const groups = drawGroups(layout, byId, sketch, ink, symbols);
      const segments = [];
      layout.parts.forEach(place => {
        const part = byId[String(place.id).toLowerCase()] || { id: place.id, name: place.id };
        const icon = deviceIcon(part, place.x, place.y);
        const drawn = place.upright ? `<g transform="rotate(90 ${place.x} ${place.y})">${icon}</g>` : icon;
        const value = symbolValue(part, volts);
        const name = referenceName(part, sketch.parts || []);
        const role = partRole(part);
        const frameW = Math.min(place.width, 64);
        const frameH = Math.min(place.height, 44);
        const frame = role
          ? `<rect x="${place.x - frameW / 2}" y="${place.y - frameH / 2}" width="${frameW}" height="${frameH}" rx="4" fill="none" stroke="${roleTone(role)}" stroke-width="1.5" />`
          : "";
        marks.push(`<g class="symbol"><title>${esc(hoverText(part))}</title>${frame}${drawn}</g>`);
        symbols.push({ x: place.x - frameW / 2 - 6, y: place.y - frameH / 2 - 6, w: frameW + 12, h: frameH + 12 });
        (place.pins || []).forEach(pin => {
          const lead = pinLead(place, pin);
          if (lead) marks.push(`<path d="${lead}" fill="none" stroke="var(--silk)" stroke-width="1.25" />`);
          marks.push(`<g><title>${esc(pin.name)}</title><circle cx="${pin.x}" cy="${pin.y}" r="3" fill="var(--text)" /></g>`);
          ink.push({ x: pin.x, y: pin.y });
        });
        captions.push({ name, value: value && !isBlock(part) ? value : "", place, frameW, frameH });
        ink.push({ x: place.x - place.width / 2 - 8, y: place.y - place.height / 2 - 28 }, { x: place.x + place.width / 2 + 48, y: place.y + place.height / 2 + 36 });
      });
      const drawn = [];
      const schematic = viewMode === "schematic";
      (layout.wires || []).forEach(wire => {
        const points = orthogonalPoints(wire.points || []);
        if (points.length < 2) return;
        const path = points.map((point, index) => `${index === 0 ? "M" : "L"}${point.x},${point.y}`).join(" ");
        wires.push(trace(path, wire.role || "signal"));
        for (let index = 1; index < points.length; index += 1) {
          segments.push({ x1: points[index - 1].x, y1: points[index - 1].y, x2: points[index].x, y2: points[index].y });
        }
        points.forEach(point => ink.push(point));
        if (schematic && wire.role === "ground") {
          const ends = [points[0], points[points.length - 1]].filter(point => onPin(point, layout));
          if (ends.length) drawGroundEnds(ends.length === 1 ? [ends[0], ends[0]] : ends, wires, ink, segments, layout);
        } else if (schematic && wire.role === "power") {
          const ends = [points[0], points[points.length - 1]].filter(point => onPin(point, layout));
          const at = (ends.length ? ends : [points[0]]).reduce((best, point) => point.y < best.y ? point : best);
          drawPowerFlags(wire, [at, at], wires, ink, segments, symbols);
        } else if (schematic) {
          const name = signalNetName(wire);
          if (name && labelSignal(wire, points, byId)) drawNetTag(wire, points, wires, ink, name, symbols);
        }
        drawn.push(wire);
      });
      if (!schematic) {
        const power = drawn.filter(wire => wire.role === "power").flatMap(wire => wire.points || []);
        if (power.length) {
          const top = power.reduce((best, point) => point.y < best.y ? point : best, power[0]);
          const powerWire = drawn.find(wire => wire.role === "power") || {};
          const label = [powerWire.from, powerWire.to].map(end => {
            const text = String(end || "");
            return text.includes(".") ? text.split(".").pop() : text;
          }).find(end => /^(3v3|3\.3v|\+?3v|\+?5v|vcc|vdd|vin)$/i.test(end)) || "3V3";
          const labelY = top.y - 14;
          wires.push(trace(`M${top.x},${top.y} L${top.x},${labelY}`, "power"));
          wires.push(caption(top.x, labelY - 2, label, "power", "middle"));
        }
        const grounds = drawn.filter(wire => wire.role === "ground").flatMap(wire => wire.points || []);
        if (grounds.length) {
          const bottom = grounds.reduce((best, point) => point.y > best.y ? point : best, grounds[0]);
          wires.push(ground(bottom.x, bottom.y));
        }
      }
      captions.forEach(item => {
        const text = item.value ? `${item.name}  ${item.value}` : item.name;
        const spot = clearCaption({ ...item, name: text }, symbols, segments);
        marks.push(caption(spot.x, spot.y, text, "text", spot.anchor));
        ink.push({ x: spot.box.x, y: spot.box.y }, { x: spot.box.x + spot.box.w, y: spot.box.y + spot.box.h });
        symbols.push(spot.box);
      });
      const right = Math.max(...ink.map(point => point.x), 160);
      const bottomEdge = Math.max(...ink.map(point => point.y), 140) + 36;
      const left = Math.min(0, ...ink.map(point => point.x)) - 36;
      const topEdge = Math.min(0, ...ink.map(point => point.y)) - 36;
      return `<svg width="${Math.max(right - left + 24, 1)}" height="${Math.max(bottomEdge - topEdge, 1)}" viewBox="${left} ${topEdge} ${right - left + 24} ${bottomEdge - topEdge}" xmlns="http://www.w3.org/2000/svg">${groups}${wires.join("")}${marks.join("")}</svg>`;
    }

    function orthogonalPoints(points) {
      if (!points.length) return [];
      const out = [{ x: points[0].x, y: points[0].y }];
      points.slice(1).forEach(next => {
        const prev = out[out.length - 1];
        if (prev.x !== next.x && prev.y !== next.y) out.push({ x: prev.x, y: next.y });
        if (out[out.length - 1].x !== next.x || out[out.length - 1].y !== next.y) out.push({ x: next.x, y: next.y });
      });
      return out;
    }

    function pinLead(place, pin) {
      if (Math.hypot(pin.x - place.x, pin.y - place.y) <= 28) return "";
      const side = pin.side || "";
      if (side === "left" || side === "right") {
        const x = side === "left" ? place.x - 14 : place.x + 14;
        if (Math.abs(pin.y - place.y) < 2) return `M${x},${place.y} L${pin.x},${pin.y}`;
        return `M${x},${place.y} L${pin.x},${place.y} L${pin.x},${pin.y}`;
      }
      if (side === "top" || side === "bottom") {
        const y = side === "top" ? place.y - 14 : place.y + 14;
        if (Math.abs(pin.x - place.x) < 2) return `M${place.x},${y} L${pin.x},${pin.y}`;
        return `M${place.x},${y} L${place.x},${pin.y} L${pin.x},${pin.y}`;
      }
      return "";
    }

    function textBox(x, y, text, anchor) {
      const width = Math.max(18, String(text).length * 6.6);
      const left = anchor === "middle" ? x - width / 2 : anchor === "end" ? x - width : x;
      return { x: left - 4, y: y - 13, w: width + 8, h: 16 };
    }

    function rectsOverlap(a, b) {
      return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
    }

    function rectHitsSegment(box, seg, pad) {
      const left = box.x - pad;
      const right = box.x + box.w + pad;
      const top = box.y - pad;
      const bottom = box.y + box.h + pad;
      const x1 = seg.x1;
      const y1 = seg.y1;
      const x2 = seg.x2;
      const y2 = seg.y2;
      if (x1 === x2) return x1 >= left && x1 <= right && Math.max(y1, y2) >= top && Math.min(y1, y2) <= bottom;
      if (y1 === y2) return y1 >= top && y1 <= bottom && Math.max(x1, x2) >= left && Math.min(x1, x2) <= right;
      return true;
    }

    function clearCaption(item, symbols, segments) {
      const place = item.place;
      const candidates = [
        { x: place.x, y: place.y - item.frameH / 2 - 22, anchor: "middle" },
        { x: place.x + item.frameW / 2 + 12, y: place.y + 4, anchor: "start" },
        { x: place.x - item.frameW / 2 - 12, y: place.y + 4, anchor: "end" },
        { x: place.x, y: place.y + item.frameH / 2 + 24, anchor: "middle" },
        { x: place.x + item.frameW / 2 + 12, y: place.y - item.frameH / 2 - 6, anchor: "start" },
      ];
      const placed = candidates.map(spot => {
        const box = textBox(spot.x, spot.y, item.name, spot.anchor);
        const hit = segments.some(seg => rectHitsSegment(box, seg, 8)) || symbols.some(sym => rectsOverlap(box, sym));
        return { ...spot, box, hit };
      });
      return placed.find(spot => !spot.hit) || placed[2];
    }

    function drawGroups(layout, byId, sketch, ink, symbols) {
      const touchesFet = id => (sketch.wires || []).some(wire => {
        const ends = [wire.from, wire.to].map(end => String(end || "").split(".")[0].toLowerCase());
        if (!ends.includes(String(id).toLowerCase())) return false;
        return ends.some(end => {
          const other = byId[end];
          return other && /mosfet|nmos|transistor|\bbjt\b/i.test(`${other.type || ""} ${other.name || ""}`);
        });
      });
      const boxes = new Map();
      (layout.parts || []).forEach(place => {
        const part = byId[String(place.id).toLowerCase()] || { id: place.id };
        const text = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
        let role = partRole(part);
        if (!role && /resistor|\bohm\b/.test(text) && (/gate/.test(text) || touchesFet(part.id))) role = "Driver";
        if (!role || role === "Power") return;
        const box = boxes.get(role) || { left: Infinity, right: -Infinity, top: Infinity, bottom: -Infinity };
        const halfW = Math.min(place.width, 72) / 2;
        const halfH = Math.min(place.height, 48) / 2;
        box.left = Math.min(box.left, place.x - halfW);
        box.right = Math.max(box.right, place.x + halfW);
        box.top = Math.min(box.top, place.y - halfH);
        box.bottom = Math.max(box.bottom, place.y + halfH);
        boxes.set(role, box);
      });
      if (boxes.has("Protection") && boxes.has("Load")) {
        const diode = boxes.get("Protection");
        const load = boxes.get("Load");
        load.left = Math.min(load.left, diode.left);
        load.right = Math.max(load.right, diode.right);
        load.top = Math.min(load.top, diode.top);
        load.bottom = Math.max(load.bottom, diode.bottom);
        boxes.delete("Protection");
      }
      return [...boxes.entries()].map(([role, box]) => {
        const x = box.left - 30;
        const y = box.top - 38;
        const width = box.right - box.left + 60;
        const height = box.bottom - box.top + 68;
        const tone = roleTone(role);
        if (ink) ink.push({ x, y }, { x: x + width, y: y + height });
        if (symbols) symbols.push({ x: x + 8, y: y + 2, w: role.length * 7 + 12, h: 16 });
        return `<g class="region"><rect x="${x}" y="${y}" width="${width}" height="${height}" rx="10" fill="${tone}" fill-opacity="0.05" stroke="${tone}" stroke-width="1" stroke-opacity="0.4" stroke-dasharray="5 4" /><text x="${x + 10}" y="${y + 16}" fill="${tone}" fill-opacity="0.75" font-size="11">${role}</text></g>`;
      }).join("");
    }

    function drawLogic(sketch) {
      const parts = sketch.parts || [];
      const ofRole = role => parts.filter(part => partRole(part) === role);
      const sensors = ofRole("Sensor");
      const inputs = ofRole("Input");
      const controllers = ofRole("Controller");
      const drivers = ofRole("Driver");
      const loads = ofRole("Load");
      const column = [];
      if (sensors.length) column.push({ role: "Sensor", labels: sensors.map(logicTitle) });
      if (controllers.length) column.push({ role: "Controller", labels: controllers.map(logicTitle) });
      if (drivers.length) column.push({ role: "Driver", labels: [driverTitle(drivers)] });
      if (loads.length) column.push({ role: "Load", labels: loads.map(logicTitle) });
      if (!column.length) {
        const named = parts.filter(part => partRole(part) !== "Power" && partRole(part) !== "Protection");
        if (!named.length) return "";
        column.push({ role: "", labels: named.map(part => part.name || part.id) });
      }
      const boxW = 168;
      const boxH = 72;
      const gapX = 24;
      const gapY = 72;
      const inputGap = inputs.length ? 210 : 0;
      const widest = Math.max(...column.map(rank => rank.labels.length));
      const columnWidth = widest * boxW + (widest - 1) * gapX;
      const left = 32;
      const top = 28;
      const columnX = left + inputGap;
      const marks = [];
      const placed = column.map((rank, index) => {
        const rowWidth = rank.labels.length * boxW + (rank.labels.length - 1) * gapX;
        const x0 = columnX + (columnWidth - rowWidth) / 2;
        const y = top + index * (boxH + gapY);
        const boxes = rank.labels.map((label, item) => {
          const x = x0 + item * (boxW + gapX);
          marks.push(logicBox(x, y, boxW, boxH, label, rank.role));
          return { x, y, cx: x + boxW / 2, top: y, bottom: y + boxH };
        });
        return { role: rank.role, boxes };
      });
      for (let index = 0; index < placed.length - 1; index += 1) {
        const upper = placed[index];
        const lower = placed[index + 1];
        const label = logicLink(upper.role, lower.role);
        if (upper.boxes.length === 1 && lower.boxes.length === 1) {
          marks.push(logicArrow(upper.boxes[0].cx, upper.boxes[0].bottom, lower.boxes[0].cx, lower.boxes[0].top, label));
        } else {
          upper.boxes.forEach(box => {
            const target = lower.boxes.reduce((best, item) => Math.abs(item.cx - box.cx) < Math.abs(best.cx - box.cx) ? item : best, lower.boxes[0]);
            marks.push(logicArrow(box.cx, box.bottom, target.cx, target.top, label));
          });
        }
      }
      const controller = placed.find(rank => rank.role === "Controller");
      if (controller && inputs.length) {
        const host = controller.boxes[0];
        inputs.forEach((part, index) => {
          const x = left;
          const y = host.top + index * (boxH + 16);
          marks.push(logicBox(x, y, boxW, boxH, logicTitle(part), "Input"));
          marks.push(logicArrow(x + boxW, y + boxH / 2, host.x, host.top + boxH / 2, "Switch"));
        });
      }
      const columnBottom = top + placed.length * (boxH + gapY);
      const inputBottom = controller && inputs.length ? controller.boxes[0].top + inputs.length * (boxH + 16) : 0;
      const width = columnX + columnWidth + 48;
      const height = Math.max(columnBottom, inputBottom) + 24;
      return `<svg width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Logic view">${marks.join("")}</svg>`;
    }

    function logicTitle(part) {
      const text = `${part.type || ""} ${part.name || ""}`.toLowerCase();
      const role = partRole(part);
      if (role === "Sensor" && /temperature|thermistor|lm35|tmp36|ntc/.test(text)) return /lm35/.test(text) ? "LM35" : "Temperature Sensor";
      if (role === "Input") return /button/.test(text) ? "Button" : "Switch";
      if (role === "Load" && /solenoid/.test(text)) return "Solenoid";
      if (role === "Load" && /fan/.test(text)) return "Fan";
      if (role === "Load" && /motor/.test(text)) return "Motor";
      if (role === "Load" && /pump/.test(text)) return "Pump";
      if (role === "Load" && /relay/.test(text)) return "Relay";
      if (role === "Load" && /led/.test(text)) return "LED";
      return referenceName(part, [part]);
    }

    function driverTitle(parts) {
      const text = parts.map(part => `${part.type || ""} ${part.name || ""}`).join(" ").toLowerCase();
      if (/mosfet|nmos/.test(text)) return "MOSFET Driver";
      if (/transistor|npn|pnp|bjt/.test(text)) return "Transistor Driver";
      return "Driver";
    }

    function logicLink(from, to) {
      if (from === "Sensor" && to === "Controller") return "Analog";
      if (from === "Controller" && to === "Driver") return "Input";
      if (from === "Driver" && to === "Load") return "";
      if (from === "Sensor" && to === "Driver") return "Analog";
      if (from === "Controller" && to === "Load") return "Output";
      return "";
    }

    function logicLines(label) {
      const words = String(label || "").split(/\s+/).filter(Boolean);
      if (words.length < 2 || label.length <= 12) return [label];
      const mid = Math.ceil(words.length / 2);
      return [words.slice(0, mid).join(" "), words.slice(mid).join(" ")];
    }

    function logicBox(x, y, w, h, label, role) {
      const lines = logicLines(label);
      const text = lines.map((line, index) => {
        const dy = (index - (lines.length - 1) / 2) * 16;
        return `<text x="${x + w / 2}" y="${y + h / 2 + dy + 4}" text-anchor="middle" fill="var(--text)" font-size="14">${esc(line)}</text>`;
      }).join("");
      const stroke = role ? roleTone(role) : "var(--line)";
      return `<g><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="8" fill="#1c2421" stroke="${stroke}" stroke-width="2" />${text}</g>`;
    }

    function logicArrow(x1, y1, x2, y2, label) {
      const horizontal = Math.abs(x2 - x1) > Math.abs(y2 - y1);
      const tip = 7;
      let points;
      if (horizontal) {
        const dir = x2 >= x1 ? 1 : -1;
        const end = x2 - dir * 2;
        points = `${end},${y2} ${end - dir * tip},${y2 - 4} ${end - dir * tip},${y2 + 4}`;
        const line = `<line x1="${x1}" y1="${y1}" x2="${end - dir * tip}" y2="${y2}" stroke="var(--muted)" stroke-width="1.5" />`;
        const text = label ? `<text x="${(x1 + x2) / 2}" y="${Math.min(y1, y2) - 8}" text-anchor="middle" fill="var(--muted)" font-size="12">${esc(label)}</text>` : "";
        return `${line}<polygon points="${points}" fill="var(--muted)" />${text}`;
      }
      const dir = y2 >= y1 ? 1 : -1;
      const end = y2 - dir * 2;
      points = `${x2},${end} ${x2 - 4},${end - dir * tip} ${x2 + 4},${end - dir * tip}`;
      const line = `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${end - dir * tip}" stroke="var(--muted)" stroke-width="1.5" />`;
      const text = label ? `<text x="${x2 + 10}" y="${(y1 + y2) / 2}" fill="var(--muted)" font-size="12">${esc(label)}</text>` : "";
      return `${line}<polygon points="${points}" fill="var(--muted)" />${text}`;
    }

    function railBody(part) {
      const token = value => /^(3v3|3\.3v|\+3v3|\+3\.3v|vcc|vdd|vin|5v|\+5v|gnd|vss|agnd|0)$/i.test(String(value || "").trim());
      const kind = `${part.type || ""} ${part.name || ""}`.toLowerCase();
      if (/mcu|esp32|esp8266|arduino|stm32|rp2040/.test(kind) || twoLead(part)) return false;
      return token(part.id) || token(part.name);
    }

    function drawBlocks(sketch, volts) {
      const shown = (sketch.parts || []).filter(part => !railBody(part));
      if (shown.length === 0) return "";
      const places = layout(sketch);
      const labels = {};
      shown.forEach(part => {
        const place = places[part.id.toLowerCase()];
        if (place && place.label) labels[part.id.toLowerCase()] = place.label;
      });
      const visible = shown.filter(part => !labels[part.id.toLowerCase()]);
      const pitchX = 150;
      const pitchY = 104;
      const centers = visible.map(part => {
        const place = places[part.id.toLowerCase()] || { column: 0, row: 0 };
        return { part, cx: 88 + place.column * pitchX, cy: 72 + place.row * pitchY };
      });
      const boxes = centers.map(item => {
        const upright = seriesVertical(item, centers, sketch.wires || []);
        const block = isBlock(item.part);
        const span = block ? 112 : twoLead(item.part) ? 52 : 64;
        const across = block ? 58 : twoLead(item.part) ? 28 : 56;
        const w = upright ? across : span;
        const h = upright ? span : across;
        return {
          part: item.part,
          cx: item.cx,
          cy: item.cy,
          upright,
          x: item.cx - w / 2,
          y: item.cy - h / 2,
          w,
          h,
        };
      });

      spread(boxes);

      const marks = [];
      const wires = [];
      const anchors = {};
      const ink = [];
      const pinMarks = [];
      boxes.forEach(box => {
        const laid = arrangePins(box, partPins(box.part, sketch.wires || []));
        Object.assign(anchors, laid.anchors);
        pinMarks.push(laid.marks);
        ink.push(...laid.ink);
      });
      const roles = wireRoles(sketch);
      const routed = bindPins(sketch, boxes, anchors, labels).map((wire, index) => {
        const role = railRole(wire, roles.get(index) || "signal");
        return { ...wire, role };
      });
      const railTop = Math.min(...boxes.map(box => box.y)) - 36;
      const railBottom = Math.max(...boxes.map(box => box.y + box.h)) + 42;
      const powerRails = new Map();
      const groundDrops = [];
      let channel = 0;
      const railName = wire => [wire.start, wire.end].map(end => end && end.label).find(label => label && (isPowerName(label) || isGroundName(label)));
      const dropToRail = (pin, railY) => {
        const outward = pin.side === "left" ? -1 : pin.side === "right" ? 1 : 0;
        if (!outward) return [pin, { x: pin.x, y: railY }];
        channel += 1;
        const x = pin.x + outward * (18 + channel * 14);
        return [pin, { x: pin.x + outward * 14, y: pin.y }, { x, y: pin.y }, { x, y: railY }];
      };
      const schematic = viewMode === "schematic";
      const byId = {};
      (sketch.parts || []).forEach(part => { byId[String(part.id).toLowerCase()] = part; });
      routed.forEach(wire => {
        if (schematic && (wire.role === "power" || wire.role === "ground")) {
          const ends = [wire.start, wire.end].filter(end => end && !end.external && end.x != null);
          if (wire.role === "power" && ends.length) drawPowerFlags(wire, ends, wires, ink);
          else ends.forEach(end => drawGroundEnds([end, end], wires, ink));
          wire.points = null;
          return;
        }
        if (wire.role === "power") {
          const pin = wire.start.external ? wire.end : wire.start;
          const name = railName(wire) || "VCC";
          if (!powerRails.has(name)) powerRails.set(name, { y: railTop - (powerRails.size * 18), xs: [] });
          const group = powerRails.get(name);
          wire.points = pin.external ? [wire.start, wire.end] : dropToRail(pin, group.y);
          if (!pin.external) group.xs.push(wire.points[wire.points.length - 1].x);
          wire.rail = true;
          wire.junction = !pin.external;
        } else if (wire.role === "ground") {
          const pin = wire.start.external ? wire.end : wire.start;
          wire.points = pin.external ? [wire.start, wire.end] : dropToRail(pin, railBottom);
          if (!pin.external) groundDrops.push(wire.points[wire.points.length - 1].x);
          wire.rail = true;
          wire.junction = !pin.external;
        } else {
          wire.points = signalRoute(wire.start, wire.end);
          if (schematic && wire.points && wire.points.length > 1 && labelSignal(wire, wire.points, byId)) {
            drawNetEnds(wire, wire.points, wires, ink, signalNetName(wire));
            wire.points = null;
          }
        }
      });
      if (!schematic) {
        powerRails.forEach(group => {
          if (!group.xs.length) return;
          const x0 = Math.min(...group.xs);
          const x1 = Math.max(...group.xs);
          routed.push({ role: "power", rail: true, points: [{ x: x0, y: group.y }, { x: x1, y: group.y }], label: "" });
        });
        if (groundDrops.length) {
          const x0 = Math.min(...groundDrops);
          const x1 = Math.max(...groundDrops);
          routed.push({ role: "ground", rail: true, points: [{ x: x0, y: railBottom }, { x: x1, y: railBottom }], label: "" });
        }
      }
      const hops = routed.map(() => []);
      for (let left = 0; left < routed.length; left += 1) {
        for (let right = left + 1; right < routed.length; right += 1) {
          if (!routed[left].points || !routed[right].points) continue;
          if (routed[left].role === routed[right].role) continue;
          const hit = segmentCrossing(routed[left].points, routed[right].points);
          if (!hit) continue;
          const owner = hit.horizontal ? right : left;
          hops[owner].push({ x: hit.x, y: hit.y });
        }
      }
      const dots = [];
      routed.forEach((wire, index) => {
        if (!wire.points) return;
        wires.push(trace(pathWithHops(wire.points, hops[index]), wire.role));
        if (!wire.rail && wire.label) {
          const labelAt = avoidBoxes(netLabelPoint(wire.points), boxes);
          if (labelAt) {
            wires.push(caption(labelAt.x, labelAt.y, wire.label, wire.role === "bus" ? "bus" : wire.role === "analog" ? "analog" : "signal", "middle"));
            ink.push({ x: labelAt.x - wire.label.length * 3.4, y: labelAt.y - 12 }, { x: labelAt.x + wire.label.length * 3.4, y: labelAt.y + 2 });
          }
        }
        if (wire.junction) dots.push({ ...wire.points[wire.points.length - 1], role: wire.role });
      });
      powerRails.forEach((group, name) => {
        if (!group.xs.length) return;
        wires.push(caption(Math.min(...group.xs), group.y - 8, name, "power", "middle"));
        ink.push({ x: Math.min(...group.xs) - 16, y: group.y - 20 });
      });
      if (groundDrops.length) wires.push(ground(Math.min(...groundDrops), railBottom));
      dots.forEach(dot => wires.push(`<circle cx="${dot.x}" cy="${dot.y}" r="2.4" fill="${dot.role === "power" ? "var(--power)" : "var(--gnd-edge)"}" />`));

      boxes.forEach(box => {
        const icon = deviceIcon(box.part, box.cx, box.cy);
        const drawn = box.upright ? `<g transform="rotate(90 ${box.cx} ${box.cy})">${icon}</g>` : icon;
        const value = symbolValue(box.part, volts);
        const name = referenceName(box.part, sketch.parts || []);
        marks.push(`<g class="symbol"><title>${esc(hoverText(box.part))}</title>`);
        marks.push(`<rect x="${box.x - 6}" y="${box.y - 6}" width="${box.w + 12}" height="${box.h + 12}" fill="transparent" />`);
        marks.push(drawn);
        if (!isBlock(box.part)) {
          if (box.upright) {
            const aside = value || shortNote(box.part, name);
            marks.push(caption(box.x + box.w + 8, box.cy - (aside ? 2 : -4), name, "text", "start"));
            if (aside) marks.push(caption(box.x + box.w + 8, box.cy + 14, aside, "silk", "start"));
          } else {
            marks.push(caption(box.cx, box.y - 8, name, "text", "middle"));
            if (value) marks.push(caption(box.cx, box.y + box.h + 16, value, "silk", "middle"));
          }
        } else {
          marks.push(caption(box.cx, box.cy + 4, name, "text", "middle"));
        }
        marks.push("</g>");
      });

      const right = Math.max(...boxes.map(box => box.x + box.w + (boxes.some(item => item.upright) ? 112 : 36)), ...ink.map(point => point.x), 160);
      const bottom = Math.max(...boxes.map(box => box.y + box.h + 56), ...ink.map(point => point.y), 140);
      const left = Math.min(0, ...ink.map(point => point.x));
      const top = Math.min(0, ...ink.map(point => point.y));
      const width = right - left + 8;
      const height = bottom - top + 8;
      return `<svg width="${width}" height="${height}" viewBox="${left - 4} ${top - 4} ${width} ${height}" role="img" aria-label="Schematic">${wires.join("")}${marks.join("")}${pinMarks.join("")}</svg>`;
    }

    function isSwitch(part) {
      const type = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (/regulator|buck|lm2596|lm2576/.test(type)) return false;
      return /button|momentary|spst|spdt|normally[- ]open|(^|[^a-z])switch([^a-z]|$)/.test(type);
    }

    function spread(boxes) {
      for (let pass = 0; pass < 8; pass += 1) {
        let moved = false;
        for (let i = 0; i < boxes.length; i += 1) {
          for (let j = i + 1; j < boxes.length; j += 1) {
            const a = boxes[i];
            const b = boxes[j];
            const overlapX = (a.w / 2 + b.w / 2 + 48) - Math.abs(b.cx - a.cx);
            const overlapY = (a.h / 2 + b.h / 2 + 36) - Math.abs(b.cy - a.cy);
            if (overlapX <= 0 || overlapY <= 0) continue;
            if (overlapX < overlapY) b.cx += Math.sign(b.cx - a.cx || 1) * overlapX;
            else b.cy += Math.sign(b.cy - a.cy || 1) * overlapY;
            b.x = b.cx - b.w / 2;
            b.y = b.cy - b.h / 2;
            moved = true;
          }
        }
        if (!moved) break;
      }
    }

    function isBlock(part) {
      const type = `${part.type || ""} ${part.name || ""}`.toLowerCase();
      return /ecu|temperature|thermistor|tmp36|lm35|(^|[^a-z])ntc([^a-z]|$)|mcu|esp32|esp8266|arduino|stm32|rp2040/.test(type);
    }

    function twoLead(part) {
      const type = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (/mosfet|transistor|npn|pnp|bjt|mcu|esp|ecu|sensor|adder|flip|relay|motor|pump/.test(type)) return false;
      return /resistor|ohm|cap|inductor|coil|diode|schottky|led|solenoid|fuse/.test(type) || isSwitch(part);
    }

    function seriesVertical(item, centers, wires) {
      if (!twoLead(item.part)) return false;
      let upright = 0;
      let across = 0;
      wires.forEach(wire => {
        const ends = [String(wire.from || ""), String(wire.to || "")];
        const mine = ends.find(end => end.split(".")[0].toLowerCase() === item.part.id.toLowerCase());
        if (!mine) return;
        const otherToken = ends.find(end => end !== mine) || "";
        const other = centers.find(center => center.part.id.toLowerCase() === otherToken.split(".")[0].toLowerCase());
        if (!other) {
          if (isGroundName(otherToken) || isPowerName(otherToken)) return;
          upright += 1;
          return;
        }
        const dx = Math.abs(other.cx - item.cx);
        const dy = Math.abs(other.cy - item.cy);
        if (dy >= dx) upright += 1;
        else across += 1;
      });
      return upright >= across;
    }

    function shortNote(part, name) {
      let note = String(part.note || "").trim();
      if (/push\s*button|momentary|spst|normally[- ]open/i.test(note)) note = "Push Button";
      else if (/^led\b/i.test(note)) note = "LED";
      note = note.replace(/\b(\+?\d+(?:\.\d+)?\s*v|3v3|vcc|vdd|gnd)\b/ig, " ").replace(/\s+/g, " ").trim();
      if (!note || note.length > 22 || note.toLowerCase() === String(name || "").toLowerCase()) return "";
      return note;
    }

    function referenceName(part, parts) {
      const text = `${part.type || ""} ${part.name || ""}`.toLowerCase();
      const blob = `${text} ${part.note || ""} ${part.id || ""}`.toLowerCase();
      const id = String(part.id || "").toLowerCase();
      const indexOf = list => {
        const found = list.findIndex(item => String(item.id || "").toLowerCase() === id);
        return found < 0 ? 1 : found + 1;
      };
      if (/mosfet|nmos|\bnpn\b|\bpnp\b|\bbjt\b|transistor/.test(text) && !/resistor/.test(text)) {
        return "Q" + indexOf(parts.filter(item => /mosfet|nmos|transistor|\bbjt\b/i.test(`${item.type} ${item.name}`) && !/resistor/i.test(`${item.type} ${item.name}`)));
      }
      if (/led/.test(text) && !/resistor/.test(text)) {
        return "LED" + indexOf(parts.filter(item => /led/i.test(`${item.type} ${item.name}`) && !/resistor/i.test(`${item.type} ${item.name}`)));
      }
      if (/resistor|\bohm\b/.test(blob)) {
        const resistors = parts.filter(item => /resistor|\bohm\b/i.test(`${item.type} ${item.name} ${item.note}`));
        const gate = resistors.filter(item => /gate/i.test(`${item.name} ${item.note} ${item.id}`));
        const ordered = gate.concat(resistors.filter(item => !gate.includes(item)));
        return "R" + indexOf(ordered);
      }
      if (/button|switch/.test(text) && !/mosfet|transistor/.test(text)) {
        return "SW" + indexOf(parts.filter(item => /button|switch/i.test(`${item.type} ${item.name}`)));
      }
      if (/diode|flyback/.test(text) && !/led/.test(text)) {
        return "D" + indexOf(parts.filter(item => /diode|flyback/i.test(`${item.type} ${item.name}`) && !/led/i.test(`${item.type} ${item.name}`)));
      }
      if (/fan/.test(text)) {
        return "FAN" + indexOf(parts.filter(item => /fan/i.test(`${item.type} ${item.name}`)));
      }
      if (/lm35/.test(text)) return "LM35";
      if (/tmp36/.test(text)) return "TMP36";
      if (/temperature|thermistor|\bntc\b/.test(text)) return "Temperature Sensor";
      if (/esp32|esp8266/.test(text)) return "ESP32";
      if (/arduino/.test(text)) return "Arduino";
      if (/stm32/.test(text)) return "STM32";
      if (/ecu/.test(text)) return "ECU";
      if (/gnd|ground/.test(text)) return "GND";
      return part.name || part.id;
    }

    function wireReach(points) {
      let length = 0;
      for (let index = 1; index < points.length; index += 1) {
        length += Math.abs(points[index].x - points[index - 1].x) + Math.abs(points[index].y - points[index - 1].y);
      }
      return length;
    }

    function labelSignal(wire, points, byId) {
      if (wireReach(points) > 220) return true;
      const owners = [wire.from, wire.to].map(end => byId[String(end || "").split(".")[0].toLowerCase()]).filter(Boolean);
      if (owners.length < 2) return false;
      const text = part => `${part.type || ""} ${part.name || ""}`.toLowerCase();
      const sensor = part => /sensor|temperature|thermistor|lm35|tmp36|\bntc\b/.test(text(part)) && !/esp32|mcu|controller|\becu\b|arduino|stm32/.test(text(part));
      const controller = part => /esp32|mcu|arduino|stm32|rp2040|\becu\b|controller/.test(text(part));
      return owners.some(sensor) && owners.some(controller);
    }

    function signalNetName(wire) {
      const pins = [wire.from, wire.to].map(end => {
        const text = String(end || "");
        return (text.includes(".") ? text.split(".").pop() : text).trim();
      });
      const blob = pins.join(" ");
      const sensorOut = pins.some(pin => /^(out|output)$/i.test(pin));
      if (/temp/i.test(blob) || (sensorOut && /adc|ain|analog|temp/i.test(blob))) return "TEMP_OUT";
      const named = pins.find(pin => /^[A-Za-z][A-Za-z0-9_]{2,}$/.test(pin) && !/^(gnd|vcc|vdd|vss|agnd|vin|out|output|anode|cathode|gate|drain|source)$/i.test(pin));
      return named ? named.replace(/[^A-Za-z0-9]+/g, "_").toUpperCase() : "";
    }

    function onPin(point, layout) {
      return (layout.parts || []).some(part => (part.pins || []).some(pin => Math.abs(pin.x - point.x) < 8 && Math.abs(pin.y - point.y) < 8));
    }

    function drawPowerFlags(wire, points, wires, ink, segments, blocks) {
      const name = netName(wire);
      const ends = [];
      [points[0], points[points.length - 1]].forEach(at => {
        if (!at || ends.some(item => item.x === at.x && item.y === at.y)) return;
        ends.push(at);
        const tip = { x: at.x, y: at.y - 22 };
        wires.push(trace(`M${at.x},${at.y} L${tip.x},${tip.y}`, "power"));
        wires.push(caption(tip.x + 8, tip.y + 4, name, "power", "start"));
        if (segments) segments.push({ x1: at.x, y1: at.y, x2: tip.x, y2: tip.y });
        if (blocks) blocks.push({ x: tip.x + 4, y: tip.y - 12, w: name.length * 7 + 8, h: 16 });
        ink.push(at, tip, { x: tip.x + name.length * 7 + 8, y: tip.y - 12 });
      });
    }

    function netName(wire) {
      const pins = [wire.from, wire.to].map(end => {
        const text = String(end || "");
        return (text.includes(".") ? text.split(".").pop() : text).trim();
      });
      const token = pin => pin.replace(/\s+/g, "").toUpperCase();
      const tokens = pins.map(token);
      if (tokens.some(pin => pin === "12V" || pin === "+12V" || pin === "V12")) return "+12V";
      if (tokens.some(pin => pin === "3V3" || pin === "3.3V" || pin === "+3V3" || pin === "+3.3V")) return "3V3";
      if (tokens.some(pin => pin === "5V" || pin === "+5V" || pin === "V5")) return "+5V";
      if (tokens.some(pin => pin === "GND" || pin === "VSS" || pin === "AGND")) return "GND";
      if (tokens.some(pin => pin === "VCC")) return "VCC";
      if (tokens.some(pin => pin === "VDD")) return "VDD";
      return pins.find(pin => pin && !/^\d+$/.test(pin)) || "NET";
    }

    function drawNetTag(wire, points, wires, ink, name, symbols) {
      let best = null;
      let span = 0;
      for (let index = 1; index < points.length; index += 1) {
        const length = Math.abs(points[index].x - points[index - 1].x) + Math.abs(points[index].y - points[index - 1].y);
        if (length > span) {
          span = length;
          best = [points[index - 1], points[index]];
        }
      }
      if (!best || span < 36) return;
      const horizontal = best[0].y === best[1].y;
      const x = (best[0].x + best[1].x) / 2 + (horizontal ? 0 : 8);
      const y = (best[0].y + best[1].y) / 2 + (horizontal ? -8 : 4);
      const anchor = horizontal ? "middle" : "start";
      const tone = wire.role === "analog" ? "analog" : "signal";
      wires.push(caption(x, y, name, tone, anchor));
      const box = textBox(x, y, name, anchor);
      if (symbols) symbols.push(box);
      ink.push({ x: box.x, y: box.y }, { x: box.x + box.w, y: box.y + box.h });
    }

    function drawGroundEnds(points, wires, ink, segments) {
      const ends = [];
      [points[0], points[points.length - 1]].forEach(at => {
        if (ends.some(item => item.x === at.x && item.y === at.y)) return;
        ends.push(at);
        wires.push(trace(`M${at.x},${at.y} L${at.x},${at.y + 8}`, "ground"));
        wires.push(ground(at.x, at.y + 8));
        if (segments) segments.push({ x1: at.x, y1: at.y, x2: at.x, y2: at.y + 20 });
        ink.push(at, { x: at.x - 16, y: at.y + 24 }, { x: at.x + 16, y: at.y + 24 });
      });
    }

    function drawNetEnds(wire, points, wires, ink, name, segments) {
      const labelName = name || netName(wire);
      const tone = wire.role === "power" ? "power" : wire.role === "ground" ? "gnd" : wire.role === "analog" ? "analog" : "signal";
      [[points[0], points[1]], [points[points.length - 1], points[points.length - 2]]].forEach(([at, toward]) => {
        const horizontal = Math.abs(toward.x - at.x) >= Math.abs(toward.y - at.y);
        const ux = horizontal ? Math.sign(toward.x - at.x || 1) : 0;
        const uy = horizontal ? 0 : Math.sign(toward.y - at.y || 1);
        const tip = { x: at.x + ux * 16, y: at.y + uy * 16 };
        wires.push(trace(`M${at.x},${at.y} L${tip.x},${tip.y}`, wire.role || "signal"));
        if (segments) segments.push({ x1: at.x, y1: at.y, x2: tip.x, y2: tip.y });
        const anchor = ux < 0 ? "end" : ux > 0 ? "start" : "middle";
        const label = { x: tip.x + ux * 6, y: tip.y + (uy < 0 ? -6 : uy > 0 ? 14 : -6) };
        wires.push(caption(label.x, label.y, labelName, tone, anchor));
        ink.push(at, tip, label);
      });
    }

export function symbolValue(part, volts) {
      const kind = `${part.type || ""} ${part.name || ""}`.toLowerCase();
      const text = `${part.name || ""} ${part.note || ""}`;
      if (volts != null && volts !== undefined && /vout|\bnode\b|junction/.test(kind)) {
        return `${Number(volts).toFixed(2)} V`;
      }
      const device = text.match(/\b(2N\d{3,4}|IRLZ\d+[A-Z]?|IRF\d+[A-Z]?|BSS\d+|AO\d+)\b/i);
      if (device && /mosfet|transistor|bjt|nmos/.test(kind)) return device[1].toUpperCase();
      if (/mosfet|nmos/.test(kind)) return "N-MOSFET";
      const cell = text.match(/(\d+(?:\.\d+)?)\s*v\b/i);
      if (cell && /source|supply|battery|vsource/.test(kind)) return cell[0];
      const ohm = text.match(/(\d+(?:\.\d+)?)\s*(k|m)?\s*(?:ohms?|Ω)/i);
      if (ohm) return `${ohm[1]}${(ohm[2] || "").toLowerCase()}Ω`;
      const unit = text.match(/(\d+(?:\.\d+)?)\s*[µμunpm]?[fh]\b/i);
      if (unit && /cap|inductor|coil/.test(kind)) return unit[0].replace(/µ/g, "μ");
      return "";
    }

    function hoverText(part) {
      const pins = (part.pins || []).map(pin => `Pin ${pin}`);
      return [part.name || part.id, part.note || "", ...pins].filter(Boolean).join("\n");
    }

    function partPins(part, wires) {
      const seen = [];
      const add = pin => {
        const text = String(pin || "").trim();
        if (!text || seen.some(item => item.toLowerCase() === text.toLowerCase())) return;
        seen.push(text);
      };
      (part.pins || []).forEach(add);
      (wires || []).forEach(wire => {
        [wire.from, wire.to].forEach(end => {
          const text = String(end || "").trim();
          const dot = text.indexOf(".");
          if (dot < 1) return;
          if (text.slice(0, dot).toLowerCase() !== String(part.id || "").toLowerCase()) return;
          add(text.slice(dot + 1));
        });
      });
      if (twoLead(part)) defaultPins(part).forEach(add);
      else if (seen.length === 0) defaultPins(part).forEach(add);
      return seen.slice(0, 8);
    }

    function defaultPins(part) {
      const type = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (/vsource|\bsource\b|\bsupply\b|\bbattery\b/.test(type) && !isSwitch(part)) return [];
      if (/\bground\b|\bgnd\b/.test(type)) return [];
      if (type.includes("led")) return ["Anode", "Cathode"];
      if (type.includes("schottky") || type.includes("diode")) return ["A", "K"];
      if (/mosfet|nmos/.test(type)) return ["Gate", "Drain", "Source"];
      if (/pnp|npn|transistor|bjt/.test(type)) return ["Base", "Collector", "Emitter"];
      if (isSwitch(part) || twoLead(part)) return ["1", "2"];
      if (isBlock(part)) return ["VCC", "GND"];
      return [];
    }

    function symbolFrame(box) {
      const type = `${box.part.type || ""} ${box.part.name || ""} ${box.part.note || ""}`.toLowerCase();
      if (isBlock(box.part)) return { x: box.cx - 56, y: box.cy - 29, w: 112, h: 58 };
      if (twoLead(box.part)) return { x: box.x, y: box.y, w: box.w, h: box.h };
      if (/mosfet|nmos|npn|pnp|transistor|bjt/.test(type)) return { x: box.cx - 24, y: box.cy - 24, w: 40, h: 48 };
      return { x: box.cx - 16, y: box.cy - 14, w: 32, h: 28 };
    }

    function electricalSide(pin, part) {
      const name = String(pin || "").trim();
      if (/^(vcc|vdd|vin|vm|\+?5v|3v3|3\.3v)$/i.test(name)) return "top";
      if (/^(gnd|vss|agnd)$/i.test(name)) return "bottom";
      const type = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (/mosfet|nmos|npn|pnp|transistor|bjt/.test(type)) {
        if (/^(gate|base|g|b)$/i.test(name)) return "left";
        if (/^(drain|collector|d|c)$/i.test(name)) return "top";
        if (/^(source|emitter|s|e)$/i.test(name)) return "bottom";
      }
      return "";
    }

    function pinPoint(frame, side, index, count) {
      const along = count === 1 ? 0.5 : (index + 1) / (count + 1);
      if (side === "top") return { x: frame.x + frame.w * along, y: frame.y, side };
      if (side === "bottom") return { x: frame.x + frame.w * along, y: frame.y + frame.h, side };
      if (side === "left") return { x: frame.x, y: frame.y + frame.h * along, side };
      return { x: frame.x + frame.w, y: frame.y + frame.h * along, side };
    }

    function arrangePins(box, pins) {
      const frame = symbolFrame(box);
      const buckets = { top: [], bottom: [], left: [], right: [] };
      const anchors = {};
      const marks = [];
      const ink = [];
      if (twoLead(box.part) && pins.length >= 2) {
        const kind = `${box.part.type || ""} ${box.part.name || ""} ${box.part.note || ""}`.toLowerCase();
        const led = kind.includes("led");
        if (box.upright) {
          buckets.top.push(pins[0]);
          buckets.bottom.push(pins[1]);
        } else {
          const left = led ? box.cx - 28 : box.cx - 26;
          const right = led ? box.cx + 24 : box.cx + 26;
          const leads = [
            [pins[0], { x: left, y: box.cy, side: "left" }],
            [pins[1], { x: right, y: box.cy, side: "right" }],
          ];
          leads.forEach(([pin, point]) => {
            anchors[`${box.part.id}.${pin}`.toLowerCase()] = point;
            const drawn = pinLabel(point, pin, point.side, false);
            marks.push(drawn.mark);
            ink.push(drawn.start, drawn.end);
          });
          return { anchors, marks: marks.join(""), ink };
        }
      } else {
        const rest = [];
        pins.forEach(pin => {
          const side = electricalSide(pin, box.part);
          if (side) buckets[side].push(pin);
          else rest.push(pin);
        });
        rest.forEach(pin => buckets.right.push(pin));
      }
      const stub = !twoLead(box.part);
      Object.entries(buckets).forEach(([side, list]) => {
        list.forEach((pin, index) => {
          const point = pinPoint(frame, side, index, list.length);
          anchors[`${box.part.id}.${pin}`.toLowerCase()] = point;
          const drawn = pinLabel(point, pin, side, stub);
          marks.push(drawn.mark);
          ink.push(drawn.start, drawn.end);
        });
      });
      return { anchors, marks: marks.join(""), ink };
    }

    function pinLabel(point, text, side, stub) {
      const dot = `<circle cx="${point.x}" cy="${point.y}" r="2" fill="var(--text)" />`;
      let leader = "";
      let x = point.x;
      let y = point.y;
      let anchor = "start";
      if (side === "left") {
        const reach = stub ? 14 : 0;
        if (reach) leader = `<line x1="${point.x - reach}" y1="${point.y}" x2="${point.x}" y2="${point.y}" stroke="var(--text)" stroke-width="1.5" />`;
        x = point.x - reach - 4;
        y = point.y - 6;
        anchor = "end";
      } else if (side === "right") {
        const reach = stub ? 14 : 0;
        if (reach) leader = `<line x1="${point.x}" y1="${point.y}" x2="${point.x + reach}" y2="${point.y}" stroke="var(--text)" stroke-width="1.5" />`;
        x = point.x + reach + 4;
        y = point.y - 6;
        anchor = "start";
      } else if (side === "top") {
        const reach = stub ? 12 : 0;
        if (reach) leader = `<line x1="${point.x}" y1="${point.y - reach}" x2="${point.x}" y2="${point.y}" stroke="var(--text)" stroke-width="1.5" />`;
        x = point.x + 6;
        y = point.y - reach - 4;
        anchor = "start";
      } else {
        const reach = stub ? 12 : 0;
        if (reach) leader = `<line x1="${point.x}" y1="${point.y}" x2="${point.x}" y2="${point.y + reach}" stroke="var(--text)" stroke-width="1.5" />`;
        x = point.x + 6;
        y = point.y + reach + 12;
        anchor = "start";
      }
      const width = Math.max(12, String(text).length * 6.6);
      const left = anchor === "end" ? x - width : x;
      const plate = `<rect x="${left - 3}" y="${y - 11}" width="${width + 6}" height="14" rx="2" fill="#141816" />`;
      return {
        mark: `${leader}${dot}${plate}<text x="${x}" y="${y}" fill="var(--text)" font-size="12" font-weight="600" text-anchor="${anchor}">${esc(text)}</text>`,
        start: { x: left - 3, y: y - 11 },
        end: { x: left + width + 6, y: y + 3 },
      };
    }

    function wireRoles(sketch) {
      const wires = (sketch.wires || []).map(wire => ({
        from: String(wire.from || "").trim(),
        to: String(wire.to || "").trim(),
      }));
      (sketch.parts || []).forEach(part => {
        if (!twoLead(part)) return;
        const id = part.id.toLowerCase();
        const hits = [];
        wires.forEach((wire, index) => {
          if (wire.from.split(".")[0].toLowerCase() === id || wire.to.split(".")[0].toLowerCase() === id) hits.push(index);
        });
        if (hits.length !== 2) return;
        const pins = new Set(hits.map(index => {
          const wire = wires[index];
          return (wire.from.split(".")[0].toLowerCase() === id ? wire.from : wire.to).toLowerCase();
        }));
        if (pins.size >= 2) return;
        hits.forEach((index, slot) => {
          const lead = `${part.id}.${slot === 0 ? "1" : "2"}`;
          const wire = wires[index];
          wires[index] = {
            from: wire.from.toLowerCase() === id ? lead : wire.from,
            to: wire.to.toLowerCase() === id ? lead : wire.to,
          };
        });
      });
      const parent = new Map();
      const find = key => {
        const name = String(key || "").trim().toLowerCase();
        if (!name) return "";
        if (!parent.has(name)) parent.set(name, name);
        let root = name;
        while (parent.get(root) !== root) root = parent.get(root);
        parent.set(name, root);
        return root;
      };
      wires.forEach(wire => {
        const left = find(wire.from);
        const right = find(wire.to);
        if (left && right && left !== right) parent.set(left, right);
      });
      const groups = new Map();
      for (const key of parent.keys()) {
        const root = find(key);
        if (!groups.has(root)) groups.set(root, []);
        groups.get(root).push(key);
      }
      const roleOf = new Map();
      for (const keys of groups.values()) {
        const found = keys.map(key => endpointRole(key, sketch.parts || []));
        const rank = ["ground", "power", "bus", "analog", "digital"];
        const role = rank.find(item => found.includes(item)) || "signal";
        keys.forEach(key => roleOf.set(key, role));
      }
      const roles = new Map();
      wires.forEach((wire, index) => roles.set(index, roleOf.get(find(wire.from)) || "signal"));
      return roles;
    }

    function endpointRole(token, parts) {
      const text = String(token || "").trim();
      const id = text.split(".")[0];
      const pin = text.includes(".") ? text.slice(id.length + 1) : "";
      const part = parts.find(item => item.id.toLowerCase() === id.toLowerCase());
      const kind = part ? `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase() : "";
      if (isGroundName(text) || isGroundName(pin) || /ground|(^|\s)gnd(\s|$)/.test(kind)) return "ground";
      if (isPowerName(text) || isPowerName(pin) || /vsource|supply|battery/.test(kind)) return "power";
      if (/^(sda|scl|mosi|miso|sck|sclk|sdi|sdo|cs|ss)$/i.test(pin)) return "bus";
      if (/^(adc\d*|a\d+|ain\d*|out|output|temp|analog)$/i.test(pin)) return "analog";
      if (/gpio|pwm|(^io\d+$)|(^d\d+$)|^(g|gate|b|base)$/i.test(pin)) return "digital";
      return "";
    }

    function isPowerName(text) {
      return /^(vcc|vdd|vin|3v3|\+?3\.3v|\+?\d+(?:\.\d+)?v)$/i.test(String(text || "").trim());
    }

    function isGroundName(text) {
      return /^(gnd|vss|agnd|0)$/i.test(String(text || "").trim());
    }

    function layoutColumns(sketch) {
      const parts = sketch.parts || [];
      const wires = sketch.wires || [];
      const byId = {};
      parts.forEach(part => { byId[part.id.toLowerCase()] = part; });
      const kindOf = part => `${part.type || ""} ${part.name || ""}`.toLowerCase();
      const isGroundPart = part => /ground|gnd/.test(kindOf(part));
      const isSupplyPart = part => /vsource|source|supply|battery/.test(kindOf(part));
      const isCapacitor = part => /cap/.test(kindOf(part));
      const isResistor = part => /resistor|ohm/.test(kindOf(part));
      const partId = end => {
        const text = String(end || "").trim();
        const token = text.split(".")[0].toLowerCase();
        return byId[token] ? byId[token].id.toLowerCase() : "";
      };
      const groundEnd = end => /^(gnd|vss|agnd|0)$/i.test(String(end || "").trim()) || isGroundPart(byId[partId(end)] || { type: "" });
      const supplyEnd = end => /^(vcc|vdd|vin)$/i.test(String(end || "").trim()) || isSupplyPart(byId[partId(end)] || { type: "" });
      const neighbors = {};
      parts.forEach(part => { neighbors[part.id.toLowerCase()] = new Set(); });
      wires.forEach(wire => {
        const left = partId(wire.from);
        const right = partId(wire.to);
        if (left && right && left !== right) {
          neighbors[left].add(right);
          neighbors[right].add(left);
        }
      });
      const touchesGround = id => wires.some(wire => {
        const ends = [wire.from, wire.to];
        return ends.some(end => partId(end) === id) && ends.some(groundEnd);
      });
      const shunt = new Set();
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (!touchesGround(id)) return;
        const others = [...neighbors[id]].filter(next => !isGroundPart(byId[next]));
        if (isCapacitor(part)) {
          shunt.add(id);
          return;
        }
        if (!isResistor(part) || others.length !== 1) return;
        const upstream = others[0];
        const continues = [...neighbors[upstream]].some(next => next !== id && !isGroundPart(byId[next]));
        if (continues) shunt.add(id);
      });
      const column = {};
      const queue = [];
      parts.forEach(part => {
        if (!isSupplyPart(part)) return;
        column[part.id.toLowerCase()] = 0;
        queue.push(part.id.toLowerCase());
      });
      wires.forEach(wire => {
        const ends = [wire.from, wire.to];
        ends.forEach((end, index) => {
          if (!supplyEnd(end)) return;
          const other = partId(ends[1 - index]);
          if (other && column[other] == null && !shunt.has(other)) {
            column[other] = 1;
            queue.push(other);
          }
        });
      });
      while (queue.length) {
        const id = queue.shift();
        neighbors[id].forEach(next => {
          if (shunt.has(next) || isGroundPart(byId[next]) || column[next] != null) return;
          column[next] = column[id] + 1;
          queue.push(next);
        });
      }
      let spare = Math.max(-1, ...Object.values(column)) + 1;
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (column[id] == null && !shunt.has(id) && !isGroundPart(part)) column[id] = spare++;
      });
      shunt.forEach(id => {
        const host = [...neighbors[id]].find(next => column[next] != null && !isGroundPart(byId[next]));
        column[id] = host == null ? spare : column[host];
      });
      const row = {};
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        row[id] = shunt.has(id) ? 1 : 0;
      });
      parts.filter(isGroundPart).forEach(part => {
        const id = part.id.toLowerCase();
        const host = [...neighbors[id]].find(next => shunt.has(next)) || [...neighbors[id]].find(next => column[next] != null);
        column[id] = host == null ? 0 : column[host];
        row[id] = host != null && shunt.has(host) ? 2 : 1;
      });
      return Object.fromEntries(parts.map(part => [part.id.toLowerCase(), { column: column[part.id.toLowerCase()] || 0, row: row[part.id.toLowerCase()] || 0 }]));
    }

    function layout(sketch) {
      return layoutGpioRows(sketch) || layoutSeries(sketch) || layoutNets(sketch) || layoutColumns(sketch);
    }

    function layoutGpioRows(sketch) {
      const parts = sketch.parts || [];
      const wires = sketch.wires || [];
      const controller = parts.filter(part => /mcu|esp32|esp8266|arduino|stm32|rp2040/.test(`${part.type || ""} ${part.name || ""}`.toLowerCase()));
      if (controller.length !== 1) return null;
      const host = controller[0];
      const hostId = host.id.toLowerCase();
      const idOf = end => String(end || "").trim().split(".")[0].toLowerCase();
      const pinOf = end => {
        const text = String(end || "").trim();
        const dot = text.indexOf(".");
        return dot < 0 ? "" : text.slice(dot + 1);
      };
      const signalPin = pin => /^(gpio\d*|io\d+|d\d+|adc\d*|a\d+|sda|scl|mosi|miso|sck|sclk|sdi|sdo|cs|ss)$/i.test(pin);
      if (parts.some(part => part.id.toLowerCase() !== hostId && !twoLead(part) && !railBody(part))) return null;
      const neighbors = {};
      parts.forEach(part => {
        if (twoLead(part)) neighbors[part.id.toLowerCase()] = [];
      });
      const outside = {};
      wires.forEach(wire => {
        const left = idOf(wire.from);
        const right = idOf(wire.to);
        const leftLead = Boolean(neighbors[left]);
        const rightLead = Boolean(neighbors[right]);
        if (leftLead && rightLead && left !== right) {
          neighbors[left].push(right);
          neighbors[right].push(left);
        } else if (leftLead) (outside[left] || (outside[left] = [])).push(String(wire.to || "").trim());
        else if (rightLead) (outside[right] || (outside[right] = [])).push(String(wire.from || "").trim());
      });
      const gpioToken = list => (list || []).find(token => idOf(token) === hostId && signalPin(pinOf(token)));
      const used = new Set();
      const rows = [];
      Object.keys(neighbors).forEach(start => {
        if (used.has(start) || !gpioToken(outside[start])) return;
        const order = [];
        const seen = new Set();
        let current = start;
        while (current && neighbors[current] && !seen.has(current)) {
          seen.add(current);
          used.add(current);
          order.push(current);
          current = neighbors[current].find(item => neighbors[item] && !seen.has(item)) || "";
        }
        rows.push(order);
      });
      if (!rows.length || Object.keys(neighbors).some(id => !used.has(id))) return null;
      const places = {
        [hostId]: { column: 0, row: (rows.length - 1) / 2 },
      };
      rows.forEach((order, row) => {
        order.forEach((id, column) => {
          places[id] = { column: column + 1, row };
        });
      });
      return places;
    }

    function layoutSeries(sketch) {
      const parts = sketch.parts || [];
      const wires = sketch.wires || [];
      const textOf = part => `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      const supplyPart = part => /vsource|\bsource\b|\bsupply\b|\bbattery\b/.test(textOf(part)) && !isSwitch(part);
      const groundPart = part => /\bground\b|\bgnd\b/.test(textOf(part));
      const chain = parts.filter(part => twoLead(part) && !supplyPart(part) && !groundPart(part));
      if (chain.length < 2 || chain.length > 6) return null;
      if (parts.some(part => !chain.includes(part) && !supplyPart(part) && !groundPart(part))) return null;
      const idOf = end => String(end || "").trim().split(".")[0].toLowerCase();
      const byId = {};
      parts.forEach(part => { byId[part.id.toLowerCase()] = part; });
      const neighbors = {};
      chain.forEach(part => { neighbors[part.id.toLowerCase()] = []; });
      wires.forEach(wire => {
        const left = idOf(wire.from);
        const right = idOf(wire.to);
        const leftChain = Boolean(neighbors[left]);
        const rightChain = Boolean(neighbors[right]);
        if (leftChain && rightChain && left !== right) {
          neighbors[left].push(right);
          neighbors[right].push(left);
        } else if (leftChain) neighbors[left].push(String(wire.to || "").trim());
        else if (rightChain) neighbors[right].push(String(wire.from || "").trim());
      });
      const isSupplyEnd = token => /^(\+?\d+(?:\.\d+)?v|vcc|vdd|vin)$/i.test(token) || (byId[idOf(token)] && supplyPart(byId[idOf(token)]));
      const isGroundEnd = token => /^(gnd|vss|agnd|0)$/i.test(token) || (byId[idOf(token)] && groundPart(byId[idOf(token)]));
      const start = chain.find(part => neighbors[part.id.toLowerCase()].some(isSupplyEnd));
      if (!start) return null;
      const order = [];
      const seen = new Set();
      let current = start.id.toLowerCase();
      while (current && neighbors[current] && !seen.has(current)) {
        seen.add(current);
        order.push(current);
        current = neighbors[current].find(item => neighbors[item] && !seen.has(item)) || "";
      }
      if (order.length !== chain.length || !neighbors[order[order.length - 1]].some(isGroundEnd)) return null;
      const places = Object.fromEntries(order.map((id, row) => [id, { column: 0, row }]));
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (places[id]) return;
        if (supplyPart(part)) places[id] = { label: voltageLabel(part) };
        else if (groundPart(part)) places[id] = { label: "GND" };
      });
      return places;
    }

    function voltageLabel(part) {
      const found = `${part.name || ""} ${part.note || ""}`.match(/(\+)?(\d+(?:\.\d+)?)\s*v/i);
      return found ? `${found[1] || "+"}${found[2]}V` : "+5V";
    }

    function layoutNets(sketch) {
      const parts = sketch.parts || [];
      const wires = sketch.wires || [];
      if (parts.length === 0 || wires.length === 0) return null;
      const byId = {};
      parts.forEach(part => { byId[part.id.toLowerCase()] = part; });
      const kind = part => `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      const isGroundPart = part => /\bground\b|\bgnd\b/.test(kind(part));
      const isSupplyPart = part => /vsource|\bsource\b|\bsupply\b|\bbattery\b/.test(kind(part));
      const isResistor = part => /resistor|\bohm\b|ω/.test(kind(part));
      const isCapacitor = part => /\bcap/.test(kind(part));
      const isController = part => /mcu|esp32|esp8266|arduino/.test(kind(part));
      const crowded = part => /sensor|oled|adder|flip|driver|tb6612|l298|l293/.test(kind(part));
      const key = end => String(end || "").trim().toLowerCase();
      const parent = {};
      const find = id => {
        if (!parent[id]) parent[id] = id;
        if (parent[id] !== id) parent[id] = find(parent[id]);
        return parent[id];
      };
      const unite = (a, b) => {
        const left = find(a);
        const right = find(b);
        if (left !== right) parent[right] = left;
      };
      wires.forEach(wire => unite(key(wire.from), key(wire.to)));
      const members = {};
      wires.forEach(wire => [wire.from, wire.to].forEach(end => {
        const root = find(key(end));
        if (!members[root]) members[root] = [];
        members[root].push(key(end));
      }));
      const pinMatches = (pin, names) => {
        const text = String(pin || "").toLowerCase();
        return names.some(name => name.length <= 2 ? text === name : text.includes(name));
      };
      const ownerOf = end => {
        const token = key(end).split(".")[0];
        return byId[token] ? token : "";
      };
      const terminals = part => {
        const id = part.id.toLowerCase();
        const seen = new Map();
        wires.forEach(wire => [wire.from, wire.to].forEach(end => {
          if (ownerOf(end) !== id) return;
          const text = key(end);
          const pin = text.includes(".") ? text.slice(text.indexOf(".") + 1) : "";
          const net = find(text);
          if (!seen.has(net)) seen.set(net, pin);
        }));
        return [...seen.entries()].map(([net, pin]) => ({ net, pin }));
      };
      const isGroundEnd = end => /^(gnd|vss|agnd|0)$/.test(key(end)) || isGroundPart(byId[ownerOf(end)] || { type: "" });
      const isSupplyEnd = end => /^(vcc|vdd|vin)$/.test(key(end)) || isSupplyPart(byId[ownerOf(end)] || { type: "" });
      const isGroundNet = net => (members[find(net)] || []).some(isGroundEnd);
      const isSupplyNet = net => !isGroundNet(net) && (members[find(net)] || []).some(isSupplyEnd);
      const netHasPin = (net, names) => parts.some(part => terminals(part).some(term => term.net === find(net) && pinMatches(term.pin, names)));
      const channel = (part, terms) => {
        if (terms.length < 2) return null;
        if (terms.length === 2) return [terms[0].net, terms[1].net];
        const findTerm = names => terms.find(term => pinMatches(term.pin, names));
        const pair = (left, right) => {
          const a = findTerm(left);
          const b = findTerm(right);
          return a && b && a.net !== b.net ? [a.net, b.net] : null;
        };
        return pair(["d", "drain"], ["s", "source"])
          || pair(["c", "collector"], ["e", "emitter"])
          || pair(["vin"], ["out", "sw", "switch"])
          || null;
      };
      const weight = part => {
        const text = kind(part);
        if (/motor|inductor|coil|led|relay|lamp|solenoid/.test(text)) return 6;
        if (/mosfet|transistor|npn|pnp|bjt|regulator|lm2596/.test(text)) return 5;
        if (/diode|schottky/.test(text)) return 4;
        if (isResistor(part)) return 2;
        return 1;
      };
      const edges = [];
      parts.forEach(part => {
        const terms = terminals(part);
        if (isController(part) || crowded(part) || terms.length > 3 || isCapacitor(part)) return;
        if (isResistor(part) && terms.some(term => netHasPin(term.net, ["g", "gate", "b", "base"]))) return;
        const nets = channel(part, terms);
        if (!nets || nets[0] === nets[1]) return;
        edges.push({ partId: part.id.toLowerCase(), a: nets[0], b: nets[1], w: weight(part) });
      });
      const from = net => edges.filter(edge => edge.a === net || edge.b === net);
      const other = (edge, net) => edge.a === net ? edge.b : edge.a;
      const search = origin => {
        const best = new Map();
        const walk = (net, dist, seen) => {
          if (seen.has(net) || seen.size > 8) return;
          best.set(net, Math.max(best.get(net) ?? -1, dist));
          seen.add(net);
          from(net).forEach(edge => walk(other(edge, net), dist + 1, seen));
          seen.delete(net);
        };
        walk(origin, 0, new Set());
        return best;
      };
      const supplies = [...new Set(Object.keys(members).map(find))].filter(isSupplyNet);
      let origin = null;
      let rank = null;
      let groundDist = -1;
      supplies.forEach(supply => {
        const reached = search(supply);
        const ground = [...reached.keys()].filter(isGroundNet).reduce((far, net) => Math.max(far, reached.get(net) ?? 0), -1);
        if (ground > groundDist) {
          groundDist = ground;
          origin = supply;
          rank = reached;
        }
      });
      if (!origin || !rank || groundDist < 1) return null;
      const spineNets = [origin];
      const spineParts = [];
      let current = origin;
      const claimed = new Set();
      while (!isGroundNet(current)) {
        const step = from(current)
          .filter(edge => !claimed.has(edge.partId))
          .map(edge => ({ edge, next: other(edge, current) }))
          .filter(item => rank.get(item.next) === (rank.get(current) ?? 0) + 1)
          .sort((left, right) => right.edge.w - left.edge.w)[0];
        if (!step) break;
        claimed.add(step.edge.partId);
        spineParts.push(step.edge.partId);
        current = step.next;
        spineNets.push(current);
      }
      if (spineParts.length === 0) return null;
      const places = {};
      const taken = new Set();
      const spineCol = 4;
      const put = (part, column, row, direction) => {
        const id = part.id.toLowerCase();
        if (places[id]) return;
        let col = column;
        for (let guard = 0; guard < 12 && taken.has(`${col},${row}`); guard += 1) col += direction;
        taken.add(`${col},${row}`);
        places[id] = { column: col, row };
      };
      let row = 0;
      parts.filter(part => isSupplyPart(part) && terminals(part).some(term => term.net === origin)).forEach((part, index) => put(part, spineCol + index, row, 1));
      if (Object.keys(places).length) row += 1;
      for (let index = 0; index < spineNets.length - 1; index += 1) {
        const high = spineNets[index];
        const low = spineNets[index + 1];
        const between = edges
          .filter(edge => (edge.a === high && edge.b === low) || (edge.a === low && edge.b === high))
          .sort((left, right) => right.w - left.w);
        between.forEach(edge => {
          claimed.add(edge.partId);
          put(byId[edge.partId], spineCol, row, 1);
        });
        row += 1;
      }
      if (isGroundNet(current)) {
        parts.filter(part => isGroundPart(part) && terminals(part).some(term => term.net === current)).forEach(part => put(part, spineCol, row, 1));
      }
      if (!parts.some(part => places[part.id.toLowerCase()] && isGroundPart(part))) {
        const bottom = Math.max(row, ...Object.values(places).map(place => place.row));
        parts.filter(isGroundPart).forEach(part => put(part, spineCol, bottom + 1, 1));
      }
      const onSpine = net => spineNets.includes(net);
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (places[id] || isController(part)) return;
        const terms = terminals(part);
        if (terms.length !== 2) return;
        const ground = terms.find(term => isGroundNet(term.net));
        const live = terms.find(term => term !== ground && onSpine(term.net));
        if (!ground || !live) return;
        const hosts = parts.filter(host => places[host.id.toLowerCase()] && terminals(host).some(term => term.net === live.net));
        const host = hosts.sort((left, right) => places[right.id.toLowerCase()].row - places[left.id.toLowerCase()].row)[0];
        if (!host) return;
        const spot = places[host.id.toLowerCase()];
        put(part, spot.column + 1, spot.row, 1);
      });
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (places[id] || isSupplyPart(part) || isGroundPart(part)) return;
        const terms = terminals(part);
        if (terms.length !== 1 || !onSpine(terms[0].net)) return;
        const hosts = parts.filter(host => places[host.id.toLowerCase()] && terminals(host).some(term => term.net === terms[0].net));
        const host = hosts.sort((left, right) => places[right.id.toLowerCase()].row - places[left.id.toLowerCase()].row)[0];
        if (!host) return;
        const spot = places[host.id.toLowerCase()];
        put(part, spot.column + 1, spot.row, 1);
      });
      const queue = [];
      const seen = new Set();
      const enqueue = anchorId => {
        const anchor = byId[anchorId];
        if (!anchor) return;
        terminals(anchor).forEach(term => {
          if (isGroundNet(term.net)) return;
          parts.forEach(part => {
            const id = part.id.toLowerCase();
            if (places[id] || seen.has(id)) return;
            if (!terminals(part).some(item => item.net === term.net)) return;
            queue.push({ id, anchor: anchorId });
          });
        });
      };
      Object.keys(places).forEach(enqueue);
      while (queue.length) {
        const item = queue.shift();
        if (places[item.id] || seen.has(item.id)) continue;
        seen.add(item.id);
        const anchor = places[item.anchor];
        if (!anchor) continue;
        const part = byId[item.id];
        const terms = terminals(part);
        const shunt = !isController(part) && terms.length === 2 && isResistor(part) && terms.some(term => isGroundNet(term.net));
        put(part, anchor.column - 1, anchor.row + (shunt ? 1 : 0), -1);
        enqueue(item.id);
      }
      let spare = Math.max(...Object.values(places).map(place => place.column), spineCol) + 1;
      parts.forEach(part => {
        const id = part.id.toLowerCase();
        if (!places[id]) put(part, spare++, 0, 1);
      });
      const shift = Math.min(...Object.values(places).map(place => place.column));
      Object.values(places).forEach(place => { place.column -= shift; });
      return places;
    }

    function deviceIcon(part, cx, cy) {
      const type = `${part.type || ""} ${part.name || ""} ${part.note || ""}`.toLowerCase();
      if (type.includes("resistor") || type.includes("ohm")) return iconResistor(cx, cy);
      if (type.includes("inductor") || type.includes("coil") || /\d\s*[uµμ]h/.test(type)) return iconInductor(cx, cy);
      if (type.includes("cap")) return iconCapacitor(cx, cy);
      if (type.includes("schottky") || type.includes("diode")) return iconDiode(cx, cy);
      if (type.includes("ground") || type.includes("gnd")) return ground(cx, cy);
      if (/(^|[^a-z])node([^a-z]|$)/.test(type) || type.includes("junction")) return `<circle cx="${cx}" cy="${cy}" r="5" fill="var(--net)" />`;
      if (type.includes("mosfet") || type.includes("nmos")) return iconMosfet(cx, cy);
      if (type.includes("pnp")) return iconTransistor(cx, cy, true);
      if (type.includes("npn") || type.includes("transistor") || type.includes("bjt") || type.includes("2n") || /(^|\s)q\d/.test(type)) return iconTransistor(cx, cy, false);
      if (type.includes("vsource") || type.includes("source") || type.includes("supply") || type.includes("battery")) {
        return `<circle cx="${cx}" cy="${cy}" r="16" fill="none" stroke="var(--silk)" stroke-width="1.5" />
          <text x="${cx}" y="${cy + 4}" text-anchor="middle" fill="var(--silk)" font-size="12">+</text>`;
      }
      if (type.includes("solenoid")) return iconInductor(cx, cy);
      if (type.includes("led")) return iconLed(cx, cy);
      if (type.includes("adder")) return iconAdder(cx, cy);
      if (/temperature|thermistor|lm35|tmp36|\bntc\b|sensor/.test(type) && !/esp|ecu|mcu|controller/.test(type)) return iconSensor(cx, cy);
      if (type.includes("mcu") || type.includes("esp") || type.includes("ecu") || type.includes("controller") || type.includes("arduino") || type.includes("stm32")) return iconChip(cx, cy);
      if (isSwitch(part)) return iconSwitch(cx, cy);
      if (type.includes("flip")) return iconFlop(cx, cy);
      return iconChip(cx, cy);
    }

    function iconResistor(cx, cy) {
      const x = cx - 26;
      return `<polyline points="${x},${cy} ${x + 8},${cy} ${x + 14},${cy - 8} ${x + 22},${cy + 8} ${x + 30},${cy - 8} ${x + 38},${cy + 8} ${x + 44},${cy} ${x + 52},${cy}" fill="none" stroke="var(--silk)" stroke-width="1.75" stroke-linejoin="round" stroke-linecap="round" />`;
    }

    function iconTransistor(cx, cy, pnp) {
      const bar = cx - 6;
      const arrow = pnp
        ? `<polygon points="${bar + 1},${cy + 6} ${cx + 7},${cy + 3} ${cx + 4},${cy + 11}" fill="var(--silk)" />`
        : `<polygon points="${cx + 3},${cy + 9} ${cx + 13},${cy + 15} ${cx + 5},${cy + 17}" fill="var(--silk)" />`;
      return `<line x1="${cx - 24}" y1="${cy}" x2="${bar}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${bar}" y1="${cy - 12}" x2="${bar}" y2="${cy + 12}" stroke="var(--silk)" stroke-width="2.25" />
        <line x1="${bar}" y1="${cy - 6}" x2="${cx + 12}" y2="${cy - 16}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 12}" y1="${cy - 16}" x2="${cx + 12}" y2="${cy - 24}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${bar}" y1="${cy + 6}" x2="${cx + 12}" y2="${cy + 16}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 12}" y1="${cy + 16}" x2="${cx + 12}" y2="${cy + 24}" stroke="var(--silk)" stroke-width="1.5" />
        ${arrow}`;
    }

    function iconMosfet(cx, cy) {
      const gate = cx - 16;
      const channel = cx - 10;
      return `<line x1="${cx - 32}" y1="${cy}" x2="${gate}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${gate}" y1="${cy - 12}" x2="${gate}" y2="${cy + 12}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${channel}" y1="${cy - 12}" x2="${channel}" y2="${cy - 4}" stroke="var(--silk)" stroke-width="2.25" />
        <line x1="${channel}" y1="${cy - 1}" x2="${channel}" y2="${cy + 1}" stroke="var(--silk)" stroke-width="2.25" />
        <line x1="${channel}" y1="${cy + 4}" x2="${channel}" y2="${cy + 12}" stroke="var(--silk)" stroke-width="2.25" />
        <line x1="${channel}" y1="${cy - 8}" x2="${cx}" y2="${cy - 8}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx}" y1="${cy - 8}" x2="${cx}" y2="${cy - 24}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${channel}" y1="${cy + 8}" x2="${cx}" y2="${cy + 8}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx}" y1="${cy + 8}" x2="${cx}" y2="${cy + 24}" stroke="var(--silk)" stroke-width="1.5" />
        <polygon points="${cx - 6},${cy + 4} ${cx},${cy + 8} ${cx - 6},${cy + 12}" fill="var(--silk)" />`;
    }

    function iconLed(cx, cy) {
      return `<line x1="${cx - 28}" y1="${cy}" x2="${cx - 14}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <polygon points="${cx - 14},${cy - 12} ${cx - 14},${cy + 12} ${cx + 8},${cy}" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 8}" y1="${cy - 12}" x2="${cx + 8}" y2="${cy + 12}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 8}" y1="${cy}" x2="${cx + 24}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 4}" y1="${cy - 18}" x2="${cx + 12}" y2="${cy - 26}" stroke="var(--probe)" stroke-width="1.5">
          <animate attributeName="opacity" values="0.25;1;0.25" dur="1.4s" repeatCount="indefinite" />
        </line>
        <line x1="${cx + 12}" y1="${cy - 16}" x2="${cx + 20}" y2="${cy - 24}" stroke="var(--probe)" stroke-width="1.5">
          <animate attributeName="opacity" values="1;0.25;1" dur="1.4s" repeatCount="indefinite" />
        </line>`;
    }

    function iconAdder(cx, cy) {
      return `<circle cx="${cx}" cy="${cy}" r="16" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <text x="${cx}" y="${cy + 5}" text-anchor="middle" fill="var(--silk)" font-size="16">Σ</text>`;
    }

    function iconInductor(cx, cy) {
      const x = cx - 28;
      return `<path d="M${x},${cy} h6 a6,6 0 1 1 12,0 a6,6 0 1 1 12,0 a6,6 0 1 1 12,0 a6,6 0 1 1 12,0 h6" fill="none" stroke="var(--silk)" stroke-width="1.75" />`;
    }

    function iconDiode(cx, cy) {
      return `<line x1="${cx - 28}" y1="${cy}" x2="${cx - 12}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <polygon points="${cx - 12},${cy - 12} ${cx - 12},${cy + 12} ${cx + 8},${cy}" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 8}" y1="${cy - 12}" x2="${cx + 8}" y2="${cy + 12}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 8}" y1="${cy}" x2="${cx + 24}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />`;
    }

    function iconCapacitor(cx, cy) {
      return `<line x1="${cx - 22}" y1="${cy}" x2="${cx - 4}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx - 4}" y1="${cy - 14}" x2="${cx - 4}" y2="${cy + 14}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 4}" y1="${cy - 14}" x2="${cx + 4}" y2="${cy + 14}" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 4}" y1="${cy}" x2="${cx + 22}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />`;
    }

    function iconSwitch(cx, cy) {
      return `<line x1="${cx - 26}" y1="${cy}" x2="${cx - 8}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />
        <circle cx="${cx - 8}" cy="${cy}" r="2.5" fill="var(--silk)" />
        <line x1="${cx - 8}" y1="${cy}" x2="${cx + 10}" y2="${cy - 14}" stroke="var(--silk)" stroke-width="1.5" />
        <circle cx="${cx + 12}" cy="${cy}" r="2.5" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <line x1="${cx + 12}" y1="${cy}" x2="${cx + 26}" y2="${cy}" stroke="var(--silk)" stroke-width="1.5" />`;
    }

    function iconChip(cx, cy) {
      const x = cx - 16;
      const y = cy - 14;
      return `<rect x="${x}" y="${y}" width="32" height="28" rx="2" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <circle cx="${x + 7}" cy="${y + 7}" r="2" fill="var(--probe)" />`;
    }

    function iconSensor(cx, cy) {
      return `<polygon points="${cx},${cy - 16} ${cx + 14},${cy} ${cx},${cy + 16} ${cx - 14},${cy}" fill="none" stroke="var(--silk)" stroke-width="1.5" />
        <circle cx="${cx}" cy="${cy}" r="3" fill="none" stroke="var(--probe)" stroke-width="1.5">
          <animate attributeName="r" values="3;8;3" dur="1.6s" repeatCount="indefinite" />
          <animate attributeName="opacity" values="1;0.15;1" dur="1.6s" repeatCount="indefinite" />
        </circle>`;
    }

    function iconFlop(cx, cy) {
      return `<polygon points="${cx - 18},${cy - 12} ${cx - 6},${cy} ${cx - 18},${cy + 12}" fill="none" stroke="var(--probe)" stroke-width="1.5" />
        <text x="${cx + 2}" y="${cy + 4}" fill="var(--silk)" font-size="13">Q</text>`;
    }

    function bindPins(sketch, boxes, anchors, labels) {
      const used = new Map();
      const idOf = end => String(end || "").trim().split(".")[0].toLowerCase();
      const boxOf = end => boxes.find(box => box.part.id.toLowerCase() === idOf(end));
      const claim = (box, sides) => {
        const id = box.part.id.toLowerCase();
        if (!used.has(id)) used.set(id, new Set());
        const all = Object.entries(anchors).filter(([key]) => key.startsWith(`${id}.`));
        const free = all.filter(([key]) => !used.get(id).has(key));
        const pool = free.length ? free : all;
        const chosen = sides.map(side => pool.find(([, point]) => point.side === side)).find(Boolean) || pool[0];
        if (!chosen) return { x: box.cx, y: box.cy, side: "right" };
        used.get(id).add(chosen[0]);
        return { ...chosen[1] };
      };
      const resolve = (token, other) => {
        const text = String(token || "").trim();
        const box = boxOf(text);
        if (!box) return { external: true, label: (labels && labels[idOf(text)]) || text };
        const key = text.toLowerCase();
        if (text.includes(".") && anchors[key]) return { ...anchors[key] };
        const otherText = String(other || "");
        const otherBox = boxOf(otherText);
        if (isGroundName(otherText)) return claim(box, ["bottom", "right", "left", "top"]);
        if (isPowerName(otherText)) return claim(box, ["top", "right", "left", "bottom"]);
        if (otherBox && otherBox.cx < box.cx) return claim(box, ["left", "top", "bottom", "right"]);
        if (otherBox && otherBox.cx > box.cx) return claim(box, ["right", "bottom", "top", "left"]);
        return claim(box, ["left", "right", "top", "bottom"]);
      };
      return (sketch.wires || []).map(link => {
        const start = resolve(link.from, link.to);
        const end = resolve(link.to, link.from);
        return { start, end, label: signalLabel(link), from: link.from, to: link.to };
      }).filter(wire => wire.start && wire.end);
    }

    function railRole(wire, role) {
      const label = end => String((end && end.label) || "");
      if (isGroundName(label(wire.start)) || isGroundName(label(wire.end))) return "ground";
      if (isPowerName(label(wire.start)) || isPowerName(label(wire.end))) return "power";
      return role;
    }

    function signalRoute(start, end) {
      if (Math.abs(start.y - end.y) < 2 || Math.abs(start.x - end.x) < 2) return [start, end];
      const step = point => {
        if (point.side === "left") return { x: point.x - 16, y: point.y };
        if (point.side === "right") return { x: point.x + 16, y: point.y };
        if (point.side === "top") return { x: point.x, y: point.y - 16 };
        if (point.side === "bottom") return { x: point.x, y: point.y + 16 };
        return { x: point.x, y: point.y };
      };
      const left = step(start);
      const right = step(end);
      return [start, left, { x: left.x, y: right.y }, right, end];
    }

    function avoidBoxes(point, boxes) {
      if (!point) return point;
      const hit = boxes.find(box => point.x >= box.x - 6 && point.x <= box.x + box.w + 6 && point.y >= box.y - 16 && point.y <= box.y + box.h + 22);
      return hit ? { x: point.x, y: hit.y - 16 } : point;
    }

    function signalLabel(link) {
      const named = [link.from, link.to].map(end => {
        const text = String(end || "").trim();
        const pin = text.includes(".") ? text.slice(text.indexOf(".") + 1) : text;
        return /^(gpio\d*|io\d+|d\d+|adc\d*|a\d+|sda|scl|mosi|miso|sck|sclk|sdi|sdo|cs|ss)$/i.test(pin) ? pin : "";
      }).find(Boolean);
      return named || "";
    }

    function netLabelPoint(points) {
      let best = null;
      let span = 0;
      for (let index = 0; index < points.length - 1; index += 1) {
        const from = points[index];
        const to = points[index + 1];
        const width = Math.abs(from.x - to.x);
        if (Math.abs(from.y - to.y) > 1 || width <= span) continue;
        span = width;
        best = { x: (from.x + to.x) / 2, y: Math.min(from.y, to.y) - 8 };
      }
      return best;
    }
    function segmentCrossing(left, right) {
      const lines = points => {
        const found = [];
        for (let index = 0; index < points.length - 1; index += 1) found.push([points[index], points[index + 1]]);
        return found;
      };
      const leftLines = lines(left);
      const rightLines = lines(right);
      for (let i = 0; i < leftLines.length; i += 1) {
        for (let j = 0; j < rightLines.length; j += 1) {
          const hit = crossAt(leftLines[i], rightLines[j]);
          if (!hit) continue;
          const leftHorizontal = Math.abs(leftLines[i][0].y - leftLines[i][1].y) < 1;
          return { x: hit.x, y: hit.y, horizontal: !leftHorizontal };
        }
      }
      return null;
    }

    function crossAt(first, second) {
      const horizontal = pair => Math.abs(pair[0].y - pair[1].y) < 1;
      if (horizontal(first) === horizontal(second)) return null;
      const across = horizontal(first) ? first : second;
      const down = horizontal(first) ? second : first;
      const y = across[0].y;
      const x = down[0].x;
      const within = (value, a, b) => value > Math.min(a, b) + 6 && value < Math.max(a, b) - 6;
      if (!within(x, across[0].x, across[1].x) || !within(y, down[0].y, down[1].y)) return null;
      return { x, y };
    }

    function pathWithHops(points, hops) {
      const radius = 6;
      let path = `M${points[0].x},${points[0].y}`;
      for (let index = 0; index < points.length - 1; index += 1) {
        const from = points[index];
        const to = points[index + 1];
        const horizontal = Math.abs(from.y - to.y) < 1;
        if (!horizontal) {
          path += ` L${to.x},${to.y}`;
          continue;
        }
        const direction = to.x >= from.x ? 1 : -1;
        const jumps = hops
          .filter(hop => Math.abs(hop.y - from.y) < 1 && (hop.x - from.x) * direction > radius && (to.x - hop.x) * direction > radius)
          .sort((a, b) => (a.x - b.x) * direction);
        jumps.forEach(hop => {
          const begin = hop.x - direction * radius;
          const finish = hop.x + direction * radius;
          const sweep = direction > 0 ? 0 : 1;
          path += ` L${begin},${from.y} A${radius},${radius} 0 0 ${sweep} ${finish},${from.y}`;
        });
        path += ` L${to.x},${to.y}`;
      }
      return path;
    }

    function trace(path, role) {
      const stroke = role === "power" ? "var(--power)"
        : role === "ground" ? "var(--gnd)"
        : "var(--muted)";
      const edge = role === "ground" ? `<path class="trace edge" stroke="var(--gnd-edge)" d="${path}" />` : "";
      return `${edge}<path class="trace" stroke="${stroke}" d="${path}" />`;
    }

    function ground(x, y) {
      return `<line x1="${x - 14}" y1="${y}" x2="${x + 14}" y2="${y}" stroke="var(--gnd-edge)" stroke-width="1.5" />
        <line x1="${x - 9}" y1="${y + 6}" x2="${x + 9}" y2="${y + 6}" stroke="var(--gnd-edge)" stroke-width="1.5" />
        <line x1="${x - 4}" y1="${y + 12}" x2="${x + 4}" y2="${y + 12}" stroke="var(--gnd-edge)" stroke-width="1.5" />`;
    }

    function caption(x, y, text, tone, anchor = "start", id = "") {
      const fill = tone === "power" ? "var(--power)"
        : tone === "gnd" ? "var(--gnd-edge)"
        : tone === "analog" ? "var(--analog)"
        : tone === "bus" || tone === "control" ? "var(--bus)"
        : tone === "signal" ? "var(--signal)"
        : tone === "probe" ? "var(--probe)"
        : tone === "silk" ? "var(--silk)"
        : "var(--text)";
      const idAttr = id ? ` id="${id}"` : "";
      return `<text${idAttr} x="${x}" y="${y}" fill="${fill}" font-size="11" text-anchor="${anchor}">${esc(text)}</text>`;
    }

    function esc(value) {
      return String(value).replace(/[&<>"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[character]));
    }

  