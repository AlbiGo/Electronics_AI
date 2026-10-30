export async function requestSketch(prompt, onLog) {
  const response = await fetch("/circuits/sketch", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ description: prompt })
  });
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  const result = {};
  while (true) {
    const chunk = await reader.read();
    if (chunk.done) break;
    buffer += decoder.decode(chunk.value, { stream: true });
    const events = buffer.split("\n\n");
    buffer = events.pop();
    for (const item of events) {
      const data = item.replace(/^data:\s*/m, "").trim();
      if (!data) continue;
      let payload;
      try {
        payload = JSON.parse(data);
      } catch (error) {
        onLog?.(`Could not read an event: ${error.message}`);
        continue;
      }
      if (payload.log) onLog?.(payload.log);
      if (payload.error) result.error = payload.error;
      if (payload.sketch) {
        result.sketch = payload.sketch;
        result.simulation = payload.simulation;
        result.suggestion = payload.suggestion;
        result.layout = payload.layout;
        result.choices = payload.choices;
      }
    }
  }
  if (!result.sketch && !result.error) result.error = "The simulation could not be finished.";
  return result;
}
