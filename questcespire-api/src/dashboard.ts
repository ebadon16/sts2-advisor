export function getDashboardHtml(): string {
	return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Qu'est-ce Spire? — Community Stats</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0d1117; color: #e6edf3; min-height: 100vh; }
  .header { background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); border-bottom: 1px solid #30363d; padding: 24px 32px; }
  .header h1 { font-size: 24px; font-weight: 600; margin-bottom: 4px; }
  .header h1 span { color: #f0883e; }
  .header p { color: #8b949e; font-size: 14px; }
  .stats-bar { display: flex; gap: 24px; padding: 16px 32px; background: #161b22; border-bottom: 1px solid #30363d; }
  .stat-box { text-align: center; }
  .stat-box .num { font-size: 24px; font-weight: 700; color: #58a6ff; }
  .stat-box .label { font-size: 12px; color: #8b949e; text-transform: uppercase; letter-spacing: 0.5px; }
  .controls { padding: 16px 32px; display: flex; gap: 12px; flex-wrap: wrap; align-items: center; }
  .controls select, .controls input { background: #21262d; border: 1px solid #30363d; color: #e6edf3; padding: 8px 12px; border-radius: 6px; font-size: 14px; }
  .controls select:focus, .controls input:focus { border-color: #58a6ff; outline: none; }
  .tabs { display: flex; gap: 0; padding: 0 32px; }
  .tab { padding: 10px 20px; cursor: pointer; border-bottom: 2px solid transparent; color: #8b949e; font-size: 14px; font-weight: 500; }
  .tab:hover { color: #e6edf3; }
  .tab.active { color: #f0883e; border-bottom-color: #f0883e; }
  .content { padding: 0 32px 32px; }
  table { width: 100%; border-collapse: collapse; margin-top: 12px; }
  th { text-align: left; padding: 10px 12px; font-size: 12px; color: #8b949e; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid #30363d; cursor: pointer; user-select: none; }
  th:hover { color: #e6edf3; }
  th.sorted-asc::after { content: ' \\25B2'; font-size: 10px; }
  th.sorted-desc::after { content: ' \\25BC'; font-size: 10px; }
  td { padding: 10px 12px; border-bottom: 1px solid #21262d; font-size: 14px; }
  tr:hover td { background: #161b22; }
  .name-cell { font-weight: 500; }
  .win-delta { font-weight: 700; }
  .win-delta.positive { color: #3fb950; }
  .win-delta.negative { color: #f85149; }
  .win-delta.neutral { color: #8b949e; }
  .bar-cell { position: relative; }
  .bar { height: 6px; border-radius: 3px; background: #58a6ff; min-width: 2px; }
  .sample { color: #8b949e; font-size: 12px; }
  .empty { text-align: center; padding: 60px 0; color: #8b949e; }
  .empty h3 { font-size: 18px; margin-bottom: 8px; color: #e6edf3; }
  .footer { text-align: center; padding: 24px; color: #484f58; font-size: 12px; border-top: 1px solid #21262d; }
  .footer a { color: #58a6ff; text-decoration: none; }
  .grade { display: inline-block; width: 28px; height: 28px; line-height: 28px; text-align: center; border-radius: 4px; font-weight: 700; font-size: 13px; }
  .grade-S { background: #f0883e; color: #fff; }
  .grade-A { background: #3fb950; color: #fff; }
  .grade-B { background: #58a6ff; color: #fff; }
  .grade-C { background: #8b949e; color: #fff; }
  .grade-D { background: #6e40c9; color: #fff; }
  .grade-F { background: #f85149; color: #fff; }
  @media (max-width: 768px) {
    .header, .stats-bar, .controls, .tabs, .content { padding-left: 16px; padding-right: 16px; }
    table { font-size: 13px; }
    td, th { padding: 8px 6px; }
  }
</style>
</head>
<body>

<div class="header">
  <h1><span>Qu'est-ce Spire?</span> Community Stats</h1>
  <p>Aggregated pick rates and win deltas from all players running the mod</p>
</div>

<div class="stats-bar" id="stats-bar">
  <div class="stat-box"><div class="num" id="total-runs">—</div><div class="label">Total Runs</div></div>
  <div class="stat-box"><div class="num" id="total-cards">—</div><div class="label">Cards Tracked</div></div>
  <div class="stat-box"><div class="num" id="total-relics">—</div><div class="label">Relics Tracked</div></div>
  <div class="stat-box"><div class="num" id="last-updated">—</div><div class="label">Last Updated</div></div>
</div>

<div class="controls">
  <select id="character-filter">
    <option value="">All Characters</option>
    <option value="ironclad">Ironclad</option>
    <option value="silent">Silent</option>
    <option value="defect">Defect</option>
    <option value="regent">Regent</option>
    <option value="necrobinder">Necrobinder</option>
  </select>
  <input type="text" id="search" placeholder="Search by name..." />
</div>

<div class="tabs">
  <div class="tab active" data-tab="cards">Cards</div>
  <div class="tab" data-tab="relics">Relics</div>
</div>

<div class="content">
  <div id="cards-tab">
    <table id="cards-table">
      <thead>
        <tr>
          <th data-sort="grade">Grade</th>
          <th data-sort="name">Card</th>
          <th data-sort="character">Character</th>
          <th data-sort="pick_rate">Pick Rate</th>
          <th data-sort="win_delta">Win Delta</th>
          <th data-sort="win_picked">Win% Picked</th>
          <th data-sort="win_skipped">Win% Skipped</th>
          <th data-sort="samples">Samples</th>
        </tr>
      </thead>
      <tbody id="cards-body"></tbody>
    </table>
  </div>
  <div id="relics-tab" style="display:none">
    <table id="relics-table">
      <thead>
        <tr>
          <th data-sort="grade">Grade</th>
          <th data-sort="name">Relic</th>
          <th data-sort="character">Character</th>
          <th data-sort="pick_rate">Pick Rate</th>
          <th data-sort="win_delta">Win Delta</th>
          <th data-sort="win_picked">Win% Picked</th>
          <th data-sort="win_skipped">Win% Skipped</th>
          <th data-sort="samples">Samples</th>
        </tr>
      </thead>
      <tbody id="relics-body"></tbody>
    </table>
  </div>
</div>

<div class="footer">
  Powered by <a href="https://github.com/ebadon16/sts2-advisor">Qu'est-ce Spire?</a> — Install the mod to contribute your run data
  <br><a href="https://ko-fi.com/redfred" style="color:#f0883e;">Buy me a coffee</a> if this helps your runs
</div>

<script>
let data = { card_stats: [], relic_stats: [], total_runs: 0, last_updated: null };
let currentTab = 'cards';
let sortCol = 'win_delta';
let sortDir = 'desc';

function formatId(id) {
  return id.replace(/_/g, ' ').replace(/\\b\\w/g, c => c.toUpperCase());
}

function formatPct(v) {
  return (v * 100).toFixed(1) + '%';
}

function winDeltaGrade(delta) {
  if (delta >= 0.10) return 'S';
  if (delta >= 0.05) return 'A';
  if (delta >= 0.01) return 'B';
  if (delta >= -0.02) return 'C';
  if (delta >= -0.05) return 'D';
  return 'F';
}

function renderRow(item, isRelic) {
  const name = isRelic ? item.RelicId : item.CardId;
  const winPicked = item.WinRateWhenPicked;
  const winSkipped = item.WinRateWhenSkipped;
  const delta = winPicked - winSkipped;
  const grade = winDeltaGrade(delta);
  const deltaClass = delta > 0.005 ? 'positive' : delta < -0.005 ? 'negative' : 'neutral';
  const deltaStr = (delta >= 0 ? '+' : '') + (delta * 100).toFixed(1) + '%';
  const barWidth = Math.min(item.PickRate * 100, 100);

  return '<tr>' +
    '<td><span class="grade grade-' + grade + '">' + grade + '</span></td>' +
    '<td class="name-cell">' + formatId(name) + '</td>' +
    '<td>' + (item.Character || '—') + '</td>' +
    '<td class="bar-cell"><div class="bar" style="width:' + barWidth + '%"></div> ' + formatPct(item.PickRate) + '</td>' +
    '<td class="win-delta ' + deltaClass + '">' + deltaStr + '</td>' +
    '<td>' + formatPct(winPicked) + '</td>' +
    '<td>' + formatPct(winSkipped) + '</td>' +
    '<td class="sample">' + item.SampleSize + '</td>' +
    '</tr>';
}

function getSortValue(item, col, isRelic) {
  const name = isRelic ? item.RelicId : item.CardId;
  const delta = item.WinRateWhenPicked - item.WinRateWhenSkipped;
  switch(col) {
    case 'grade': return delta;
    case 'name': return name.toLowerCase();
    case 'character': return (item.Character || '').toLowerCase();
    case 'pick_rate': return item.PickRate;
    case 'win_delta': return delta;
    case 'win_picked': return item.WinRateWhenPicked;
    case 'win_skipped': return item.WinRateWhenSkipped;
    case 'samples': return item.SampleSize;
    default: return 0;
  }
}

function render() {
  const charFilter = document.getElementById('character-filter').value;
  const search = document.getElementById('search').value.toLowerCase();

  document.getElementById('total-runs').textContent = data.total_runs.toLocaleString();
  document.getElementById('total-cards').textContent = data.card_stats.length.toLocaleString();
  document.getElementById('total-relics').textContent = data.relic_stats.length.toLocaleString();
  document.getElementById('last-updated').textContent = data.last_updated
    ? new Date(data.last_updated).toLocaleDateString()
    : 'Never';

  const isRelic = currentTab === 'relics';
  const items = isRelic ? data.relic_stats : data.card_stats;
  const filtered = items.filter(item => {
    const name = isRelic ? item.RelicId : item.CardId;
    if (charFilter && item.Character !== charFilter) return false;
    if (search && !formatId(name).toLowerCase().includes(search)) return false;
    return true;
  });

  filtered.sort((a, b) => {
    const va = getSortValue(a, sortCol, isRelic);
    const vb = getSortValue(b, sortCol, isRelic);
    const cmp = typeof va === 'string' ? va.localeCompare(vb) : va - vb;
    return sortDir === 'asc' ? cmp : -cmp;
  });

  const bodyId = isRelic ? 'relics-body' : 'cards-body';
  const body = document.getElementById(bodyId);

  if (filtered.length === 0) {
    body.innerHTML = '<tr><td colspan="8" class="empty"><h3>No data yet</h3>Install the mod and play some runs to start contributing data</td></tr>';
  } else {
    body.innerHTML = filtered.map(item => renderRow(item, isRelic)).join('');
  }

  // Update sort indicators
  document.querySelectorAll('#' + currentTab + '-tab th').forEach(th => {
    th.classList.remove('sorted-asc', 'sorted-desc');
    if (th.dataset.sort === sortCol) {
      th.classList.add(sortDir === 'asc' ? 'sorted-asc' : 'sorted-desc');
    }
  });
}

// Tab switching
document.querySelectorAll('.tab').forEach(tab => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    currentTab = tab.dataset.tab;
    document.getElementById('cards-tab').style.display = currentTab === 'cards' ? '' : 'none';
    document.getElementById('relics-tab').style.display = currentTab === 'relics' ? '' : 'none';
    sortCol = 'win_delta';
    sortDir = 'desc';
    render();
  });
});

// Sort
document.querySelectorAll('th[data-sort]').forEach(th => {
  th.addEventListener('click', () => {
    const col = th.dataset.sort;
    if (sortCol === col) {
      sortDir = sortDir === 'desc' ? 'asc' : 'desc';
    } else {
      sortCol = col;
      sortDir = col === 'name' || col === 'character' ? 'asc' : 'desc';
    }
    render();
  });
});

// Filters
document.getElementById('character-filter').addEventListener('change', render);
document.getElementById('search').addEventListener('input', render);

// Fetch data
fetch('/api/stats?min_samples=1')
  .then(r => r.json())
  .then(d => { data = d; render(); })
  .catch(() => { render(); });
</script>
</body>
</html>`;
}
