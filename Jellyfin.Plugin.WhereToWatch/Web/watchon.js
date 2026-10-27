(function () {
    'use strict';

    var BLOCK_CLASS = 'wtw-watchon';

    function isPolish() {
        var lang = (document.documentElement.getAttribute('lang') || navigator.language || 'en').toLowerCase();
        return lang.indexOf('pl') === 0;
    }

    // The server serializes with PascalCase; accept camelCase too so a settings change cannot silently break the row.
    function pick(source, name) {
        if (!source) {
            return undefined;
        }

        var camel = name.charAt(0).toLowerCase() + name.slice(1);
        return source[name] !== undefined ? source[name] : source[camel];
    }

    function itemIdFromHash() {
        var query = location.hash.split('?')[1];
        return query ? new URLSearchParams(query).get('id') : null;
    }

    function isWebUrl(value) {
        return typeof value === 'string' && /^https?:\/\//i.test(value);
    }

    function removeBlocks(page) {
        Array.prototype.forEach.call(page.querySelectorAll('.' + BLOCK_CLASS), function (element) {
            element.remove();
        });
    }

    function buildBlock(data) {
        var providers = pick(data, 'Providers') || [];
        var fallbackLink = pick(data, 'Link');

        var block = document.createElement('div');
        block.className = BLOCK_CLASS;
        block.style.cssText = 'margin: 1.2em 0;';

        var heading = document.createElement('div');
        heading.textContent = isPolish() ? 'Oglądaj na' : 'Watch on';
        heading.style.cssText = 'font-weight: 500; margin-bottom: 0.5em;';
        block.appendChild(heading);

        var row = document.createElement('div');
        row.style.cssText = 'display: flex; flex-wrap: wrap; gap: 0.6em; align-items: center;';
        providers.forEach(function (provider) {
            var id = pick(provider, 'Id');
            var name = pick(provider, 'Name') || '';

            var image = document.createElement('img');
            image.src = ApiClient.getUrl('WhereToWatch/logo/' + id);
            image.alt = name;
            image.title = name;
            image.style.cssText = 'display: block; width: 3.4em; height: 3.4em; border-radius: 0.5em; object-fit: cover;';

            // TMDB's API offers one watch page per title, not per provider, so every logo opens that page.
            if (isWebUrl(fallbackLink)) {
                var anchor = document.createElement('a');
                anchor.href = fallbackLink;
                anchor.target = '_blank';
                anchor.rel = 'noopener noreferrer';
                anchor.appendChild(image);
                row.appendChild(anchor);
            } else {
                row.appendChild(image);
            }
        });
        block.appendChild(row);

        // TMDB requires crediting JustWatch wherever its provider data is shown.
        var credit = document.createElement('div');
        credit.style.cssText = 'margin-top: 0.5em; font-size: 0.8em; opacity: 0.7;';
        credit.appendChild(document.createTextNode(isPolish() ? 'Źródło: ' : 'Data by '));
        if (isWebUrl(fallbackLink)) {
            var creditLink = document.createElement('a');
            creditLink.href = fallbackLink;
            creditLink.target = '_blank';
            creditLink.rel = 'noopener noreferrer';
            creditLink.textContent = 'JustWatch';
            credit.appendChild(creditLink);
        } else {
            credit.appendChild(document.createTextNode('JustWatch'));
        }

        block.appendChild(credit);
        return block;
    }

    function show(page) {
        // The details page element is reused for every item, so drop the previous item's row straight away.
        removeBlocks(page);

        var id = itemIdFromHash();
        if (!id || !window.ApiClient) {
            return;
        }

        ApiClient.getJSON(ApiClient.getUrl('WhereToWatch/watch/' + id)).then(function (data) {
            var providers = pick(data, 'Providers') || [];
            if (itemIdFromHash() !== id || providers.length === 0) {
                return;
            }

            removeBlocks(page);
            var content = page.querySelector('.detailSectionContent');
            if (!content) {
                return;
            }

            var block = buildBlock(data);
            var anchor = content.querySelector('.overview-controls');
            if (anchor) {
                anchor.insertAdjacentElement('afterend', block);
            } else {
                content.appendChild(block);
            }
        }).catch(function () {
            // No row is better than an error on the details page.
        });
    }

    document.addEventListener('viewshow', function (event) {
        var page = event.target;
        if (page && page.id === 'itemDetailPage') {
            show(page);
        }
    });
})();
