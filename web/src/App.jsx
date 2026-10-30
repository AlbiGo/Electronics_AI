import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react";
import { requestSketch } from "./api.js";
import { circuitRoles, renderSchematic, shownSummary, symbolValue } from "./schematic/render.js";

const roleOrder = ["Sensor", "Input", "Controller", "Driver", "Load", "Protection", "Communication", "Power"];

function checksOf(simulation) {
  const checks = simulation && simulation.checks && simulation.checks.length
    ? simulation.checks.slice()
    : [{ name: "Operating point", detail: "This schematic has no ngspice model yet.", result: "Unavailable" }];
  if (simulation && simulation.inputVolts != null) {
    checks.push({ name: "Supply voltage", detail: `${Number(simulation.inputVolts).toFixed(1)} V`, result: "Pass" });
  }
  return checks;
}

function passed(simulation) {
  const checks = simulation && simulation.checks;
  return Array.isArray(checks) && checks.length > 0 && checks.every(check => check.result === "Pass");
}

export default function App() {
  const [prompt, setPrompt] = useState("");
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("Generated circuit");
  const [summary, setSummary] = useState("The schematic appears here.");
  const [error, setError] = useState("");
  const [view, setView] = useState("schematic");
  const [sheet, setSheet] = useState(null);
  const [checks, setChecks] = useState([]);
  const [roles, setRoles] = useState([]);
  const [history, setHistory] = useState([]);
  const [active, setActive] = useState(0);
  const [tab, setTab] = useState("bom");
  const [logs, setLogs] = useState([]);
  const [choices, setChoices] = useState([]);
  const [suggestion, setSuggestion] = useState("");
  const description = useRef("");

  function log(line) {
    const time = new Date().toLocaleTimeString();
    setLogs(current => [...current, `${time}  ${line}`]);
  }

  function show(next) {
    setSheet(next);
    setTitle(next.sketch.title || "Generated circuit");
    setSummary(shownSummary(next.sketch.summary, next.volts));
    setChecks(checksOf(next.simulation));
    const found = circuitRoles(next.sketch);
    setRoles(roleOrder.filter(role => found.includes(role)));
    setChoices([]);
    setSuggestion("");
    setError("");
  }

  async function generate(revision) {
    const text = (revision ? description.current : prompt).trim();
    if (!text || busy) return;
    if (!revision) description.current = text;
    setBusy(true);
    setError("");
    setChoices([]);
    setSuggestion("");
    setSheet(null);
    setChecks([]);
    setRoles([]);
    setTitle("Generating…");
    setSummary("");
    log("Sending the request");
    let nextRevision = revision || "";
    let last = null;
    try {
      for (let attempt = 1; attempt <= 4; attempt += 1) {
        if (attempt > 1) {
          setTitle("Validation failed. Correcting the circuit…");
          log("Asking for a corrected circuit");
        }
        const posted = nextRevision ? `${text}\n\nCorrect the previous circuit. ${nextRevision}` : text;
        const payload = await requestSketch(posted, log);
        if (payload.error) {
          setError(payload.error);
          setTitle("Simulation not finished");
          return;
        }
        last = payload;
        if (payload.choices && payload.choices.length) {
          setChoices(payload.choices);
          setTitle("Choose a circuit");
          setSummary("This LED driver can be drawn three ways. Choose one.");
          log("Choose which LED driver to draw");
          return;
        }
        if (passed(payload.simulation)) {
          const next = {
            sketch: payload.sketch,
            simulation: payload.simulation,
            volts: payload.simulation && payload.simulation.outputVolts,
            layout: payload.layout,
            description: text
          };
          show(next);
          setHistory(current => [{ ...next, title: payload.sketch.title || text }, ...current].slice(0, 12));
          setActive(0);
          log("Validation passed");
          return;
        }
        const problem = payload.suggestion && payload.suggestion.text;
        log(problem || "Validation did not pass");
        if (!problem) break;
        nextRevision = problem;
      }
      setTitle("Validation did not pass");
      setError((last && last.suggestion && last.suggestion.text) || "Validation did not pass.");
      if (last && last.simulation) {
        setChecks(checksOf(last.simulation));
        const found = circuitRoles(last.sketch);
        setRoles(roleOrder.filter(role => found.includes(role)));
      }
    } catch (requestError) {
      setError(requestError.message);
      setTitle("Simulation not finished");
    } finally {
      setBusy(false);
    }
  }

  const counts = {
    passed: checks.filter(check => check.result === "Pass").length,
    failed: checks.filter(check => check.result === "Fail").length,
    warnings: checks.filter(check => check.result !== "Pass" && check.result !== "Fail").length
  };
  const canvas = useRef(null);
  const markup = sheet ? renderSchematic(sheet.sketch, sheet.volts, sheet.layout, view) : "";

  return (
    <>
      <header>
        <h1>ElectronicsAI</h1>
      </header>
      <div className="workspace">
        <aside className="side">
          <form onSubmit={event => { event.preventDefault(); generate(); }}>
            <label htmlFor="description">Prompt</label>
            <textarea id="description" value={prompt} placeholder="Create a LED circuit" onChange={event => setPrompt(event.target.value)} />
            <button className="primary" type="submit" disabled={busy}>{busy ? "Generating…" : "Generate"}</button>
          </form>
          <div className="history-label">History</div>
          <div id="history">
            {history.map((item, index) => (
              <button key={`${item.title}-${index}`} type="button" className={index === active ? "history-item active" : "history-item"} onClick={() => { setActive(index); setPrompt(item.description); show(item); }}>
                ✓ {item.title}
              </button>
            ))}
          </div>
        </aside>
        <section className="viewer">
          <div className="viewer-bar">
            <div>
              <h2>{title}</h2>
              <p className="muted">{summary}</p>
            </div>
            <Toolbar view={view} setView={setView} markup={markup} canvas={canvas} />
          </div>
          {view === "schematic" && (
            <div className="net-key" aria-label="Net colors">
              <span><i className="power" />Power</span>
              <span><i className="gnd" />Ground</span>
            </div>
          )}
          <Canvas ref={canvas} markup={markup} busy={busy} />
          {error && <p className="error">{error}</p>}
          {choices.length > 0 && (
            <div id="suggestion">
              <p>This LED driver can be drawn three ways. Choose one.</p>
              <div className="choices">
                {choices.map(choice => (
                  <button key={choice.label} type="button" onClick={() => generate(choice.prompt)}>
                    <b>{choice.label}</b><span>{choice.detail}</span>
                  </button>
                ))}
              </div>
            </div>
          )}
          {suggestion && <p id="suggestion">{suggestion}</p>}
        </section>
      </div>
      <section className="validation" aria-label="Validation results">
        <div className="validation-head">
          <div>
            <h3>Circuit health</h3>
            <p className={counts.failed ? "validation-summary health-counts bad" : "validation-summary health-counts"}>
              <span className="ok">✓ {counts.passed} passed</span>
              <span className="warn">⚠ {counts.warnings} warnings</span>
              <span className="bad">✗ {counts.failed} errors</span>
            </p>
          </div>
          <div>
            <h3>Detected roles</h3>
            <div className="roles">
              {roles.map(role => <span key={role} className={`role ${role.toLowerCase()}`}><i />{role}</span>)}
            </div>
          </div>
        </div>
        <div className="cards">
          {checks.map(check => {
            const mark = check.result === "Pass" ? "✓" : check.result === "Fail" ? "✗" : "•";
            return <article key={check.name} className={`card ${check.result}`} title={check.detail || ""}><b>{mark} {check.name}</b></article>;
          })}
        </div>
      </section>
      <div className="dock">
        <div className="tabs">
          {["bom", "netlist", "simulation", "logs"].map(name => (
            <button key={name} type="button" className={tab === name ? "active" : ""} onClick={() => setTab(name)}>{name === "bom" ? "BOM" : name[0].toUpperCase() + name.slice(1)}</button>
          ))}
        </div>
        <div className="panel" hidden={tab !== "bom"}>
          {sheet && sheet.sketch.parts && sheet.sketch.parts.length
            ? sheet.sketch.parts.map(part => (
              <div className="bom" key={part.id}><b>{part.name || part.id}</b><span>{symbolValue(part, sheet.volts) || part.type || ""}</span></div>
            ))
            : <p className="muted">No components.</p>}
        </div>
        <div className="panel" hidden={tab !== "netlist"}>
          <pre>{sheet && sheet.simulation && sheet.simulation.netlist ? sheet.simulation.netlist : "This circuit has no SPICE netlist."}</pre>
        </div>
        <div className="panel" hidden={tab !== "simulation"}>
          <Simulation simulation={sheet && sheet.simulation} sketch={sheet && sheet.sketch} />
        </div>
        <div className="panel" hidden={tab !== "logs"}>
          <pre>{logs.join("\n")}</pre>
        </div>
      </div>
    </>
  );
}

function Toolbar({ view, setView, markup, canvas }) {
  return (
    <div className="tools">
      <button type="button" className={view === "schematic" ? "active" : ""} onClick={() => setView("schematic")}>Schematic</button>
      <button type="button" className={view === "logic" ? "active" : ""} onClick={() => setView("logic")}>Logic View</button>
      <button type="button" onClick={() => canvas.current?.zoom(1.2)}>Zoom +</button>
      <button type="button" onClick={() => canvas.current?.zoom(1 / 1.2)}>Zoom −</button>
      <button type="button" onClick={() => canvas.current?.fit()}>Fit</button>
      <button type="button" onClick={() => download(markup, "svg")}>Export SVG</button>
      <button type="button" onClick={() => download(markup, "png")}>Export PNG</button>
    </div>
  );
}

const Canvas = forwardRef(function Canvas({ markup, busy }, ref) {
  const stage = useRef(null);
  const host = useRef(null);
  const drag = useRef(null);
  const [scale, setScale] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });

  function fit() {
    setScale(1);
    setPan({ x: 0, y: 0 });
  }

  useImperativeHandle(ref, () => ({
    fit,
    zoom(factor) {
      setScale(current => Math.max(0.2, Math.min(current * factor, 6)));
    }
  }));

  useEffect(() => {
    if (host.current) host.current.innerHTML = markup || "";
    const frame = requestAnimationFrame(fit);
    const box = stage.current;
    const observer = box ? new ResizeObserver(() => fit()) : null;
    if (box && observer) observer.observe(box);
    return () => {
      cancelAnimationFrame(frame);
      if (observer) observer.disconnect();
    };
  }, [markup]);

  useEffect(() => {
    const svg = host.current?.querySelector("svg");
    if (!svg) return;
    svg.style.maxWidth = "100%";
    svg.style.maxHeight = "100%";
    svg.style.width = "auto";
    svg.style.height = "auto";
    svg.style.transformOrigin = "center center";
    svg.style.transform = `translate(${pan.x}px, ${pan.y}px) scale(${scale})`;
  }, [scale, pan, markup]);

  return (
    <div
      id="stage"
      ref={stage}
      onPointerDown={event => {
        if (event.button !== 0) return;
        drag.current = { x: event.clientX, y: event.clientY, panX: pan.x, panY: pan.y };
        event.currentTarget.classList.add("panning");
        event.currentTarget.setPointerCapture(event.pointerId);
      }}
      onPointerMove={event => {
        if (!drag.current) return;
        setPan({
          x: drag.current.panX + event.clientX - drag.current.x,
          y: drag.current.panY + event.clientY - drag.current.y
        });
      }}
      onPointerUp={event => {
        drag.current = null;
        event.currentTarget.classList.remove("panning");
      }}
    >
      {busy && <div className="spinner"><span />Simulating</div>}
      <div id="schematic" ref={host} />
    </div>
  );
});

function Simulation({ simulation, sketch }) {
  if (!simulation) return <p className="muted">No simulation yet.</p>;
  const led = sketch && (sketch.parts || []).some(part => /led/i.test(`${part.type} ${part.name}`));
  const stats = [];
  if (simulation.inputVolts != null) stats.push(["Supply voltage", `${Number(simulation.inputVolts).toFixed(1)} V`]);
  if (simulation.loadCurrentAmps != null) stats.push([led ? "LED current" : "Current", `${(simulation.loadCurrentAmps * 1000).toFixed(1)} mA`]);
  if (simulation.outputVolts != null) stats.push(["Output", `${Number(simulation.outputVolts).toFixed(2)} V`]);
  return (
    <>
      <div className="stats">
        {stats.map(([name, value]) => <div className="stat" key={name}><b>{value}</b><span>{name}</span></div>)}
      </div>
      {simulation.assumptions && simulation.assumptions.length > 0 && (
        <ul className="assumptions">{simulation.assumptions.map(line => <li key={line}>{line}</li>)}</ul>
      )}
    </>
  );
}

function download(markup, kind) {
  if (!markup) return;
  const css = getComputedStyle(document.documentElement);
  const svg = markup.replace(/var\((--[\w-]+)\)/g, (_, name) => css.getPropertyValue(name).trim() || "#ccc");
  if (kind === "svg") {
    const blob = new Blob([svg], { type: "image/svg+xml" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = "schematic.svg";
    link.click();
    URL.revokeObjectURL(link.href);
    return;
  }
  const image = new Image();
  const url = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
  image.onload = () => {
    const canvas = document.createElement("canvas");
    canvas.width = image.width || 1200;
    canvas.height = image.height || 800;
    canvas.getContext("2d").drawImage(image, 0, 0);
    canvas.toBlob(blob => {
      const link = document.createElement("a");
      link.href = URL.createObjectURL(blob);
      link.download = "schematic.png";
      link.click();
    });
    URL.revokeObjectURL(url);
  };
  image.src = url;
}
