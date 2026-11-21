// Hawkeye streamer mode: polls /state 10x a second, draws the meter and skill icons where they sit on the
// streamer's screen, scaled to the OBS source size. Countdowns run smoothly between polls.
(function () {
    'use strict';

    var stage = document.getElementById('stage');
    var state = null;
    var firstInstance = null;
    var receivedAt = 0;
    var meterEl = null;
    var rowEls = {};
    var iconEls = {};
    var SKULL = '<svg viewBox="0 0 16 16"><path d="M8 1C4.1 1 1.5 3.6 1.5 7c0 2 .9 3.5 2.3 4.4V14h2v-1.5h1.2V14h2v-1.5h1.2V14h2v-2.6c1.4-.9 2.3-2.4 2.3-4.4C14.5 3.6 11.9 1 8 1zM5.3 9.2a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm5.4 0a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z"/></svg>';

    function poll() {
        fetch('/state', { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (s) {
                // A new Hawkeye run (restart or update): reload so OBS gets the new page, not the one it cached.
                if (firstInstance && s.instance && s.instance !== firstInstance) { location.reload(); return; }
                if (!firstInstance) firstInstance = s.instance;
                state = s; receivedAt = performance.now();
            })
            .catch(function () { state = null; })
            .then(function () { setTimeout(poll, 100); });
    }

    function el(tag, cls, parent) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (parent) parent.appendChild(e);
        return e;
    }

    function setText(e, t) { if (e.textContent !== t) e.textContent = t; }

    // ------------------------------------------------------------------ meter

    function buildMeter() {
        var shell = el('div', 'shell', stage);
        var logo = el('img', 'meter-logo', shell);
        logo.src = '/images/hawkeye-emblem.png';
        shell._version = el('span', 'meter-version', shell);
        var meter = el('div', 'meter', shell);
        var head = el('div', 'meter-header', meter);
        shell._target = el('span', 'meter-target', head);
        shell._timer = el('span', 'meter-timer', head);
        shell._rows = el('div', 'meter-rows', meter);
        shell._status = el('div', 'meter-status', meter);
        return shell;
    }

    function renderMeter(m) {
        if (!m) {
            if (meterEl) { meterEl.remove(); meterEl = null; rowEls = {}; }
            return;
        }
        if (!meterEl) meterEl = buildMeter();
        meterEl.style.left = m.left + 'px';
        meterEl.style.top = m.top + 'px';
        meterEl.style.width = m.width + 'px';
        meterEl.style.opacity = m.opacity;
        setText(meterEl._version, state.version);
        setText(meterEl._target, m.target);
        setText(meterEl._timer, m.duration);
        meterEl._timer.className = 'meter-timer' + (m.fromHistory ? ' is-last' : '');

        var seen = {};
        m.rows.forEach(function (r, i) {
            seen[r.id] = true;
            var row = rowEls[r.id];
            if (!row) {
                row = el('div', 'meter-row');
                row._fill = el('div', 'meter-fill', row);
                row._icon = el('div', 'meter-icon', row);
                row._name = el('span', 'meter-name', row);
                row._deaths = el('span', 'meter-deaths', row);
                row._total = el('span', 'meter-total', row);
                row._dps = el('span', 'meter-dps', row);
                rowEls[r.id] = row;
            }
            if (meterEl._rows.children[i] !== row) meterEl._rows.insertBefore(row, meterEl._rows.children[i] || null);
            row.className = 'meter-row' + (r.me ? ' is-me' : '');
            row._fill.style.width = r.bar + '%';
            row._icon.style.backgroundImage = "url('/images/classes/" + r.classId + ".png')";
            setText(row._name, r.name);
            if (r.deaths > 0) {
                var html = SKULL + r.deaths;
                if (row._deaths.innerHTML !== html) row._deaths.innerHTML = html;
                row._deaths.style.display = '';
            } else {
                row._deaths.style.display = 'none';
            }
            setText(row._total, m.showTotals ? r.total : '');
            row._total.style.display = m.showTotals ? '' : 'none';
            setText(row._dps, r.dps + '/s');
        });
        Object.keys(rowEls).forEach(function (id) {
            if (!seen[id]) { rowEls[id].remove(); delete rowEls[id]; }
        });

        var statusHtml = m.status.map(function (s) {
            return '<span class="' + s.css + '">' + s.text.replace(/</g, '&lt;') + '</span>';
        }).join('');
        if (meterEl._status.innerHTML !== statusHtml) meterEl._status.innerHTML = statusHtml;
    }

    // ------------------------------------------------------------------ skill icons

    function formatLeft(ms) {
        var s = Math.max(0, ms / 1000);
        if (s >= 60) return Math.ceil(s / 60) + 'm';
        if (s >= 2) return String(Math.ceil(s));     // whole seconds, like the game
        return s.toFixed(1);                          // one decimal in the last 2 s
    }

    function renderIcons(icons, elapsed) {
        var seen = {};
        icons.forEach(function (ic) {
            seen[ic.id] = true;
            var e = iconEls[ic.id];
            if (!e) {
                e = el('div', 'ic', stage);
                e._dim = el('div', 'dim', e);
                e._sweep = el('div', 'sweep', e);
                e._bar = el('div', 'bar', e);
                e._num = el('div', 'num', e);
                e._wasActive = false;
                e._flashUntil = 0;
                iconEls[ic.id] = e;
            }
            e.style.left = ic.left + 'px';
            e.style.top = ic.top + 'px';
            e.style.width = ic.size + 'px';
            e.style.height = ic.size + 'px';
            if (ic.icon && e._iconUrl !== ic.icon) {
                e._iconUrl = ic.icon;
                e.style.backgroundImage = "url('" + ic.icon + "')";
            }
            e._num.style.fontSize = Math.max(10, ic.size * 0.36) + 'px';

            var left = Math.max(0, ic.leftMs - elapsed);
            var active = left > 50;
            var frac = ic.totalMs > 0 ? Math.min(1, left / ic.totalMs) : 0;
            var now = performance.now();
            var cls = 'ic';

            if (ic.buff) {
                e._sweep.style.display = 'none';
                if (active) {
                    var ending = left <= 3000;
                    cls += ' buff-up' + (ending ? ' ending' : '');
                    e._dim.style.display = 'none';
                    e._bar.style.display = '';
                    e._bar.style.width = (frac * 100) + '%';
                    setText(e._num, formatLeft(left));
                    e._num.className = 'num' + (ending ? ' warn' : '');
                } else {
                    cls += ' buff-down';
                    e._dim.style.display = '';
                    e._bar.style.display = 'none';
                    setText(e._num, '');
                }
            } else {
                e._dim.style.display = 'none';
                e._bar.style.display = 'none';
                if (e._wasActive && !active) e._flashUntil = now + 700;
                if (active) {
                    cls += ' cooling';
                    var elapsedPct = (1 - frac) * 100;
                    e._sweep.style.display = '';
                    // dark over what is still to go, bright part growing clockwise from 12 o'clock
                    e._sweep.style.background = 'conic-gradient(transparent 0 ' + elapsedPct + '%, rgba(0,0,0,0.71) ' + elapsedPct + '% 100%)';
                    setText(e._num, formatLeft(left));
                    e._num.className = 'num' + (left <= 2000 ? ' warn' : '');
                } else {
                    e._sweep.style.display = 'none';
                    setText(e._num, '');
                    if (now < e._flashUntil) cls += ' flash';
                }
            }
            e._wasActive = active;
            if (e.className !== cls) e.className = cls;
        });
        Object.keys(iconEls).forEach(function (id) {
            if (!seen[id]) { iconEls[id].remove(); delete iconEls[id]; }
        });
    }

    // ------------------------------------------------------------------ Hawkeye's own windows
    // Settings, History, Extras, combat analysis: pictures taken by Hawkeye ~4x a second (/window/<id>.png).
    // A new picture is loaded off-screen first and swapped in when ready, so nothing flickers.

    var winEls = {};

    function renderWindows(list) {
        var seen = {};
        list.forEach(function (w) {
            seen[w.id] = true;
            var e = winEls[w.id];
            if (!e) {
                e = el('img', 'win', stage);
                e._ver = -1;
                e._loading = -1;
                winEls[w.id] = e;
            }
            e.style.left = w.left + 'px';
            e.style.top = w.top + 'px';
            e.style.width = w.width + 'px';
            e.style.height = w.height + 'px';
            if (w.ver !== e._ver && w.ver !== e._loading) {
                e._loading = w.ver;
                var next = new Image();
                next.onload = function () { e.src = next.src; e._ver = w.ver; };
                next.src = '/window/' + w.id + '.png?v=' + w.ver;
            }
        });
        Object.keys(winEls).forEach(function (id) {
            if (!seen[id]) { winEls[id].remove(); delete winEls[id]; }
        });
    }

    // ------------------------------------------------------------------ frame

    function frame() {
        if (state && state.screen) {
            var scale = window.innerWidth / state.screen.width;
            stage.style.width = state.screen.width + 'px';
            stage.style.height = state.screen.height + 'px';
            stage.style.transform = 'scale(' + scale + ')';
            renderMeter(state.meter);
            renderWindows(state.windows || []);
            renderIcons(state.icons || [], performance.now() - receivedAt);
        } else {
            renderMeter(null);
            renderWindows([]);
            renderIcons([], 0);
        }
        requestAnimationFrame(frame);
    }

    poll();
    requestAnimationFrame(frame);
})();
