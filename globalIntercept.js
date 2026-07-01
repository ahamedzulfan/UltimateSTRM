/* Ultimate STRM — Custom Iframe Playback Interceptor
   Drop this file into your JS-injector plugin in place of the old globalIntercept.js.
   The key change: mappings are now read from the REST API instead of the
   Jellyfin plugin XML config, so there is no GUID dependency.            */

(function () {
    let cachedMappings = [];
    let initAttempts = 0;

    console.log('[UltimateSTRM] Custom iframe interceptor loaded.');

    async function loadMappings() {
        initAttempts++;
        try {
            const res = await fetch('/UltimateStrm/iframe/mappings', {
                headers: { 'Content-Type': 'application/json' }
            });
            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }
            const list = await res.json();
            cachedMappings = (list || []).map(m => ({
                jellyfinId: (m.JellyfinId || m.jellyfinId || '').toLowerCase(),
                embedUrl: m.EmbedUrl || m.embedUrl || ''
            })).filter(m => m.jellyfinId && m.embedUrl);
            console.log('[UltimateSTRM] Iframe mappings loaded:', cachedMappings.length);
        } catch (err) {
            if (initAttempts < 10) {
                setTimeout(loadMappings, 1000);
            } else {
                console.error('[UltimateSTRM] Failed to load iframe mappings after retries:', err);
            }
        }
    }

    // Load on startup and re-load whenever the SPA navigates (keeps cache fresh).
    loadMappings();
    document.addEventListener('viewshow', loadMappings);

    // Intercept play-button clicks BEFORE Jellyfin's own handler runs.
    document.addEventListener('click', function (e) {
        const playButton = e.target.closest(
            '[data-action="play"], .btnPlay, .detailButton-play, ' +
            '.cardPlayButton, .playstatebutton, .playButton, .actionSheetMenuItem'
        );
        if (!playButton) { return; }

        const itemId = (
            playButton.getAttribute('data-id') ||
            playButton.closest('[data-id]')?.getAttribute('data-id') ||
            (window.location.href.split('id=')[1] || '').split('&')[0]
        ).toLowerCase();

        if (!itemId) { return; }

        const match = cachedMappings.find(m => m.jellyfinId === itemId);
        if (!match) { return; }

        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();

        console.log('[UltimateSTRM] Intercepted play for item:', itemId);

        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;z-index:999999;background:#000;display:flex;flex-direction:column;';

        const closeBtn = document.createElement('button');
        closeBtn.innerText = '✕  Close Player';
        closeBtn.style.cssText = 'position:absolute;top:18px;right:18px;z-index:1000000;padding:10px 18px;background:rgba(0,0,0,0.75);color:#fff;border:1px solid #555;cursor:pointer;font-weight:600;border-radius:4px;font-size:13px;';
        closeBtn.onclick = () => overlay.remove();

        const iframe = document.createElement('iframe');
        iframe.src = match.embedUrl;
        iframe.style.cssText = 'width:100%;height:100%;border:none;flex-grow:1;';
        iframe.setAttribute('allowfullscreen', 'true');
        iframe.setAttribute('allow', 'autoplay; fullscreen; encrypted-media');

        overlay.appendChild(closeBtn);
        overlay.appendChild(iframe);
        document.body.appendChild(overlay);
    }, true);
})();
