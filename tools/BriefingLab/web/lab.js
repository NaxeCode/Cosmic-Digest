"use strict";
const report = JSON.parse(document.getElementById("lab-report").textContent);
const content = document.getElementById("content");
const sourceById = new Map(report.reports.map((source) => [source.id, source]));
const caseById = new Map(
  report.cases.map((result) => [result.fixture.id, result]),
);
const format = (options) =>
  new Intl.DateTimeFormat("en-GB", { ...options, timeZone: "UTC" });
const clock = (value) =>
  format({ hour: "2-digit", minute: "2-digit" }).format(new Date(value));
const date = (value) =>
  format({ day: "2-digit", month: "short" }).format(new Date(value));
const escape = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (character) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        character
      ],
  );
const e = escape;
const caseLink = (result) => "#case/" + encodeURIComponent(result.fixture.id);
const sourceLink = (id) => "#sources/" + encodeURIComponent(id);
const tone = (result) =>
  result.fixture.knownLimit
    ? "limit"
    : { Accepted: "accepted", Fallback: "fallback", "No items": "empty" }[
        result.actual
      ];
const displayStatus = (result) =>
  result.fixture.knownLimit ? "Accepted · known limit" : result.actual;
const status = (result) =>
  `<span class="status ${tone(result)}">${e(displayStatus(result))}</span>`;
let filter = "All cases";
const filters = [
  "All cases",
  "Fallback",
  "Accepted",
  "No items",
  "Known limit",
];
const matchesFilter = (result) =>
  filter === "All cases" ||
  (filter === "Known limit"
    ? result.fixture.knownLimit
    : result.actual === filter);

function drawArrivals() {
  const end = new Date(report.clock).getTime();
  const start = end - 6 * 3600000;
  const recent = report.reports.filter(
    (source) =>
      Date.parse(source.published) >= start &&
      Date.parse(source.published) <= end,
  );
  const lanes = [
    ["Release", ["Release note", "Syndicated report"]],
    ["Operations", ["Operations correction"]],
    ["Other", ["Community notice", "Bench report"]],
  ];
  const marks = (source) => {
    const at = ((Date.parse(source.published) - start) / (end - start)) * 100;
    const label = e(
      `${clock(source.published)} UTC · ${source.kind} · ${source.source}`,
    );
    return `<a class="point" style="left:${at}%" title="${label}" aria-label="${label}" href="${sourceLink(source.id)}"></a>`;
  };
  document.getElementById("arrivals").innerHTML =
    lanes
      .map(
        ([name, kinds]) =>
          `<div class="lane"><small>${name}</small><div class="track">${recent
            .filter((s) => kinds.includes(s.kind))
            .map(marks)
            .join("")}</div></div>`,
      )
      .join("") +
    `<div class="ruler"><i></i><div>${[0, 2, 4, 6].map((h) => `<span>${clock(start + h * 3600000)}</span>`).join("")}</div></div>`;
}

function rail(selected) {
  const options = report.cases.filter(matchesFilter);
  return `<aside class="rail" aria-label="Choose a case"><h2>Case files</h2>
    <div class="filt"><label for="filter">Outcome</label><select id="filter">${filters.map((name) => `<option${name === filter ? " selected" : ""}>${e(name)}</option>`).join("")}</select></div>
    <div class="mobile-case"><label for="choose-case">Case file</label><select id="choose-case">${options
      .map(
        (item) =>
          `<option value="${e(item.fixture.id)}"${item === selected ? " selected" : ""}>${e(item.fixture.title)}</option>`,
      )
      .join("")}</select></div>
    <ul class="case-list">${options
      .map(
        (
          item,
        ) => `<li><a href="${caseLink(item)}" ${item === selected ? 'aria-current="true"' : ""}>
      <i aria-hidden="true" class="dot ${tone(item)}"></i><span>${e(item.fixture.title)}</span></a></li>`,
      )
      .join("")}</ul></aside>`;
}

function highlighted(text, quote) {
  const pos = quote ? text.indexOf(quote) : -1;
  if (pos < 0) return e(text);
  return (
    e(text.slice(0, pos)) +
    "<mark>" +
    e(quote) +
    "</mark>" +
    e(text.slice(pos + quote.length))
  );
}

function evidence(item, result) {
  const rank =
    item && report.candidates.find((c) => c.index === item.article_index);
  const source = rank && sourceById.get(rank.sourceId);
  const claim = item
    ? `<p class="claim">${e(item.what_changed)}</p><p class="label quote-label">Submitted evidence quote</p><blockquote class="proposed">${e(item.evidence_quote)}</blockquote>
       <p class="meta">Article ${e(item.article_index)} · ${e(item.decision)} · ${e(item.confidence)} confidence</p>`
    : `<p class="claim">${e(result.fixture.response.bottom_line)}</p><p class="meta">0 items submitted</p>`;
  let excerpt = `<p class="claim">${item ? "Reference outside the batch" : "No source cited"}</p><p class="meta">${item ? `Article ${e(item.article_index)} exceeds the ${report.candidates.length} ranked candidates.` : "The response selected an empty item list."}</p>`;
  if (source)
    excerpt = `<h4>${e(source.source)}</h4><p class="meta">${clock(source.published)} UTC · Article ${rank.index} · ${e(source.kind)}</p>
    <blockquote>${highlighted(source.summary, item.evidence_quote)}</blockquote><a class="source-ref" href="${sourceLink(source.id)}">Locate report in timeline</a>`;
  return `<div class="evidence"><section class="sheet"><h3>Proposed response</h3>${claim}</section><section class="sheet"><h3>Cited source</h3>${excerpt}</section></div>`;
}

function caseView(result) {
  if (!matchesFilter(result)) filter = "All cases";
  const f = result.fixture;
  const items = f.response.items;
  const pairs = (items.length ? items : [null])
    .map((item) => evidence(item, result))
    .join("");
  let title, reason;
  if (result.error) {
    title = "Validation rejected the response";
    reason = result.error;
  } else if (f.knownLimit) {
    title = "Validation accepted an unsupported claim";
    reason =
      "The excerpt occurs in the cited report. The validator checks quote attribution. It does not test whether the conclusion follows from the quote.";
  } else if (result.actual === "No items") {
    title = "Validation accepted an empty selection";
    reason =
      "The briefing would be suppressed because the validated response has no items.";
  } else {
    title = "Validation accepted the response";
    reason =
      "The submitted items satisfied the quote, article reference and decision checks.";
  }
  const displayed = result.displayed.items
    .map((item) => {
      const candidate = report.candidates[item.article_index - 1];
      const source = sourceById.get(candidate.sourceId);
      return `<li><span class="meta">Art. ${item.article_index}</span><div><strong>${e(source.title)}</strong><p>${e(item.what_changed)}</p></div></li>`;
    })
    .join("");
  content.innerHTML = `<div class="case-layout">${rail(result)}<article>
    <div class="case-head"><h2>${e(f.title)}</h2>${status(result)}</div><p class="intro">${e(f.note)}</p>
    ${pairs}<section class="decision ${tone(result)}"><h3>${e(title)}</h3><p>${e(reason)}</p></section>
    <p class="stage-readout">Structure: ${e(result.structure.toLowerCase())} · Full validation: ${result.error ? "rejected" : "accepted"}</p>
    <div class="flow-title"><h3>${result.actual === "Fallback" ? "Deterministic fallback" : "Validated briefing"}</h3><span class="meta">${result.visible.length} displayed</span></div>
    <p class="sub">${e(result.displayed.bottom_line)}</p><ol class="outcomes">${displayed}</ol>
    <details><summary>Inspect the proposed JSON</summary><pre>${e(JSON.stringify(f.response, null, 2))}</pre></details>
  </article></div>`;
  document.getElementById("filter").addEventListener("change", (event) => {
    filter = event.target.value;
    const current = matchesFilter(result)
      ? result
      : report.cases.find(matchesFilter);
    if (!current) return;
    if (current !== result) history.replaceState(null, "", caseLink(current));
    caseView(current);
    document.getElementById("filter").focus({ preventScroll: true });
  });
  document.getElementById("choose-case").addEventListener("change", (event) => {
    location.hash = caseLink(caseById.get(event.target.value));
  });
}

function sourceView(selected) {
  const entries = [...report.reports].sort(
    (a, b) => Date.parse(a.published) - Date.parse(b.published),
  );
  content.innerHTML = `<div class="section-head"><h2>Source timeline</h2>
    <p class="intro">The announcement and its correction arrived three hours apart. The ranker grouped the syndicated copy with the original and kept the correction as its own event.</p></div>
    <div class="sources-grid"><ol class="timeline">${entries
      .map(
        (source) =>
          `<li id="source-${e(source.id)}" ${source.id === selected ? 'aria-current="true"' : ""}><time datetime="${e(source.published)}">${clock(source.published)}<small>${date(source.published)}</small></time>
       <div><h3>${e(source.title)}</h3><p class="meta">${e(source.source)} · ${e(source.kind)}</p><blockquote>${e(source.summary)}</blockquote></div></li>`,
      )
      .join(
        "",
      )}</ol><aside class="ranking"><h3>Ranked candidates</h3><p>Article numbers come from the C# selector at the fixture clock.</p>
    <ol>${report.candidates.map((candidate) => `<li><strong>${e(sourceById.get(candidate.sourceId).title)}</strong><span class="meta">Score ${candidate.score.toFixed(3)} · ${candidate.publishers} publisher${candidate.publishers === 1 ? "" : "s"}</span></li>`).join("")}</ol>
    <p>A second publisher can repeat one report. Coverage count alone establishes no independent confirmation.</p></aside></div>`;
  const row = selected && document.getElementById("source-" + selected);
  if (row) row.scrollIntoView({ block: "nearest" });
}

function resultsView() {
  const fallback = report.cases.filter((r) => r.actual === "Fallback").length;
  content.innerHTML = `<div class="section-head"><h2>Run results</h2><p class="intro">Each hand-authored response runs through the production validator. An expectation checks the outcome, the rejection reason when applicable, and which reports reach the briefing.</p></div>
    <div class="stats"><span><strong>${report.matched}/${report.cases.length}</strong> expectations matched</span><span><strong>${fallback}</strong> fallbacks</span>
    <span><strong>${report.additionalRejections}</strong> extra rejections from quote checks</span><span><strong>${report.unsupportedAccepted}</strong> unsupported claim accepted</span></div>
    <table><thead><tr><th scope="col">Fixture</th><th scope="col">Structure only</th><th scope="col">Expected</th><th scope="col">Actual</th><th scope="col">Matched</th></tr></thead><tbody>${report.cases
      .map(
        (result) =>
          `<tr><td><a href="${caseLink(result)}">${e(result.fixture.title)}</a></td><td>${e(result.structure)}</td><td>${e(result.fixture.expected)}</td><td>${e(displayStatus(result))}</td><td>${result.matches ? "Yes" : "No"}</td></tr>`,
      )
      .join(
        "",
      )}</tbody></table><p class="results-note">Rank order and publisher counts matched the fixture: ${report.rankMatches ? "yes" : "no"}. These outcomes measure deterministic behavior on this corpus. Live generation quality and semantic accuracy are unmeasured.</p>
    <button id="download" class="download" type="button">Download the C# report</button>`;
  document.getElementById("download").onclick = () => {
    const url = URL.createObjectURL(
      new Blob([JSON.stringify(report, null, 2) + "\n"], {
        type: "application/json",
      }),
    );
    const link = document.createElement("a");
    link.download = "cosmic-digest-report.json";
    link.href = url;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
}

function render() {
  const [view, id] = location.hash.slice(1).split("/");
  const current = ["sources", "results"].includes(view) ? view : "case";
  document.querySelectorAll("nav a").forEach((link) => {
    if (link.dataset.view === current)
      link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  });
  if (current === "sources") sourceView(id);
  else if (current === "results") resultsView();
  else caseView(caseById.get(id) ?? report.cases[0]);
}
drawArrivals();
render();
window.addEventListener("hashchange", render);
