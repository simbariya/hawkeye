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
    var SKULL = '<svg viewBox="0 0 16 16"><path fill-rule="evenodd" d="M8 1.6c-3.3 0-5.9 2.4-5.9 5.6 0 1.9.9 3.4 2.4 4.2v2.4h7v-2.4c1.5-.8 2.4-2.3 2.4-4.2 0-3.2-2.6-5.6-5.9-5.6zM5.6 9.5a1.45 1.45 0 1 0 0-2.9 1.45 1.45 0 0 0 0 2.9zm4.8 0a1.45 1.45 0 1 0 0-2.9 1.45 1.45 0 0 0 0 2.9z"/></svg>';

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
        var meter = el('div', 'meter', shell);
        var head = el('div', 'meter-header', meter);
        shell._target = el('span', 'meter-target', head);
        shell._timer = el('span', 'meter-timer', head);
        shell._rows = el('div', 'meter-rows', meter);
        shell._pvp = el('div', 'meter-pvp', meter);
        shell._pvp.innerHTML = '<div class="pvp-kda"><div><b class="k"></b><span>Kills</span></div><div><b class="d"></b><span>Deaths</span></div><div><b class="a"></b><span>Assists</span></div></div>' +
            '<div class="pvp-gain"><span>AP</span><b class="ap"></b></div><div class="pvp-gain"><span>Rank points</span><b class="rp"></b></div>' +
            '<div class="pvp-rank"><span class="st"></span><b class="nx"></b></div>';
        meter.insertBefore(shell._pvp, shell._rows);
        shell._arena = el('div', 'meter-pvp meter-arena', meter);
        meter.insertBefore(shell._arena, shell._rows);
        shell._status = el('div', 'meter-status', meter);
        return shell;
    }

    function esc(t) { return String(t).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }

    // Arena view (v0.2.26): score, each enemy's Defiance, the session's wins and losses.
    function renderArena(box, a) {
        var html = '';
        if (a.score) {
            var cls = a.result === 'Won' ? ' is-won' : a.result === 'Lost' ? ' is-lost' : '';
            html += '<div class="arena-score' + cls + '"><b>' + esc(a.score) + '</b>' + (a.result ? '<span>' + esc(a.result) + '</span>' : '') + '</div>';
            a.foes.forEach(function (f) {
                html += '<div class="arena-foe"><div class="foe-name"><span>' + esc(f.name) + '</span><small>' + esc(f.rating) + '</small></div>' +
                    '<div class="foe-def ' + (f.down ? 'is-down' : 'is-ready') + '"><i style="width:' + f.bar + '%"></i><span>Defiance</span><b>' + esc(f.text) + '</b></div></div>';
            });
        }
        html += '<div class="pvp-kda"><div><b>' + a.wins + '</b><span>Wins</span></div><div><b>' + a.losses + '</b><span>Losses</span></div><div><b>' +
            esc(a.change) + '</b><span>Rating</span></div></div><div class="pvp-gain"><span>Arena rating</span><b>' + esc(a.rating) + '</b></div>';
        if (box._html !== html) { box.innerHTML = html; box._html = html; }
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
        var shellClass = 'shell ' + (m.look || 'theme-monolith');
        if (meterEl.className !== shellClass) meterEl.className = shellClass;
        setText(meterEl._target, m.arena ? 'Arena' : m.pvp ? 'PvP' : m.target);
        setText(meterEl._timer, m.arena ? m.arena.time : m.pvp ? m.pvp.time : m.duration);
        meterEl._arena.style.display = m.arena ? '' : 'none';
        meterEl._pvp.style.display = m.pvp && !m.arena ? '' : 'none';
        meterEl._rows.style.display = m.pvp || m.arena ? 'none' : '';
        if (m.arena) renderArena(meterEl._arena, m.arena);
        if (m.pvp) {
            setText(meterEl._pvp.querySelector('.k'), String(m.pvp.kills));
            setText(meterEl._pvp.querySelector('.d'), String(m.pvp.deaths));
            setText(meterEl._pvp.querySelector('.a'), String(m.pvp.assists));
            setText(meterEl._pvp.querySelector('.ap'), m.pvp.ap);
            setText(meterEl._pvp.querySelector('.rp'), m.pvp.rank);
            meterEl._pvp.querySelector('.pvp-rank').style.display = m.pvp.standing ? '' : 'none';
            setText(meterEl._pvp.querySelector('.st'), m.pvp.standing || '');
            setText(meterEl._pvp.querySelector('.nx'), m.pvp.toNext || '');
        }
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
            var dpsHtml = r.dps.replace(/</g, '&lt;') + '<small>/s</small>';
            if (row._dps.innerHTML !== dpsHtml) row._dps.innerHTML = dpsHtml;
        });
        Object.keys(rowEls).forEach(function (id) {
            if (!seen[id]) { rowEls[id].remove(); delete rowEls[id]; }
        });

        var statusHtml = m.status.map(function (s) {
            return '<span class="' + s.css + '">' + s.text.replace(/</g, '&lt;') + '</span>';
        }).join('') + '<span class="status-version">' + state.version + '</span>';
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

    // ------------------------------------------------------------------ Theostone alerts (v0.2.19, "Scope" style v0.2.23)
    // Drawn from /state.theo: in their own box at the streamer's spot, or over the meter, same look as on screen.

    var theoEl = null;

    function renderTheo(t) {
        if (!t || !t.items || !t.items.length) {
            if (theoEl) { theoEl.remove(); theoEl = null; }
            return;
        }
        var parent = t.where === 'meter' && meterEl ? meterEl.querySelector('.meter') : stage;
        if (!theoEl || theoEl.parentNode !== parent) {
            if (theoEl) theoEl.remove();
            theoEl = el('div', t.where === 'meter' ? 'theo-alerts' : 'theo-alerts theo-float', parent);
        }
        if (t.where !== 'meter') {
            theoEl.style.left = t.left + 'px';
            theoEl.style.top = t.top + 'px';
            theoEl.style.transform = 'scale(' + t.scale + ')';
        }
        var html = t.items.map(function (p) {
            var esc = function (s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;'); };
            var left = p.bar >= 0 ? Math.max(0, Math.min(1, p.bar / 100)) : 1;
            return '<div class="theo-alert ' + (p.yours ? 'is-yours' : 'is-on-you') + '" style="opacity:' + p.fade + '">' +
                '<div class="theo-ring"><svg viewBox="0 0 44 44">' + (p.bar >= 0 ? '<circle class="bg" cx="22" cy="22" r="20"/>' : '') +
                '<circle class="arc" cx="22" cy="22" r="20" stroke-dasharray="125.66" transform="rotate(-90 22 22)" stroke-dashoffset="' +
                (125.66 * (1 - left)).toFixed(2) + '"/></svg><img src="/images/hawkeye-emblem.png" alt=""></div>' +
                '<div class="theo-text"><span class="theo-tag">' + esc(p.tag) + '</span>' +
                '<span class="theo-name' + (p.grade >= 0 ? ' g' + p.grade : '') + '">' + esc(p.name) + '</span>' +
                '<span class="theo-num"><b class="theo-dmg">' + esc(p.amount) + '</b>' +
                (p.flags ? '<em class="theo-flag">' + esc(p.flags) + '</em>' : '') +
                (p.sub ? '<span class="theo-who">' + esc(p.sub) + '</span>' : '') + '</span></div></div>';
        }).join('');
        if (theoEl._html !== html) { theoEl.innerHTML = html; theoEl._html = html; }
    }

    // Enemy Defiance box (v0.2.27): the Theostone style, the Defiance icon in the ring.
    var defEl = null;
    function renderDefiance(d) {
        if (!d || !d.items || !d.items.length) {
            if (defEl) { defEl.remove(); defEl = null; }
            return;
        }
        if (!defEl) defEl = el('div', 'theo-alerts theo-float', stage);
        defEl.style.left = d.left + 'px';
        defEl.style.top = d.top + 'px';
        defEl.style.transform = 'scale(' + d.scale + ')';
        var html = d.items.map(function (t) {
            var left = Math.max(0, Math.min(1, t.bar / 100));
            return '<div class="theo-alert def-alert ' + (t.down ? 'is-down' : 'is-ready') + '">' +
                '<div class="theo-ring"><svg viewBox="0 0 44 44">' + (t.down ? '<circle class="bg" cx="22" cy="22" r="20"/>' : '') +
                '<circle class="arc" cx="22" cy="22" r="20" stroke-dasharray="125.66" transform="rotate(-90 22 22)" stroke-dashoffset="' +
                (125.66 * (1 - left)).toFixed(2) + '"/></svg>' + (d.icon ? '<img src="' + esc(d.icon) + '" alt="">' : '') + '</div>' +
                '<div class="theo-text"><span class="theo-tag">' + esc(t.name) + '</span><span class="theo-name">Defiance</span>' +
                '<span class="theo-num"><b class="theo-dmg">' + esc(t.main) + '</b></span></div></div>';
        }).join('');
        if (defEl._html !== html) { defEl.innerHTML = html; defEl._html = html; }
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
            renderTheo(state.theo);
            renderDefiance(state.defiance);
        } else {
            renderTheo(null);
            renderDefiance(null);
            renderMeter(null);
            renderWindows([]);
            renderIcons([], 0);
        }
        requestAnimationFrame(frame);
    }

    poll();
    requestAnimationFrame(frame);
})();
