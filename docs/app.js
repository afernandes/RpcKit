// RpcKit docs — small enhancements
(function () {
  'use strict';

  // 1. Highlight active sidebar link based on current path.
  const here = location.pathname.split('/').pop() || 'index.html';
  document.querySelectorAll('nav.toc a').forEach((a) => {
    const href = a.getAttribute('href');
    if (!href) return;
    if (href === here || (here === '' && href === 'index.html')) {
      a.classList.add('active');
    }
  });

  // 2. Copy-to-clipboard buttons on <pre><code> blocks.
  document.querySelectorAll('pre').forEach((pre) => {
    const code = pre.querySelector('code');
    if (!code) return;
    const btn = document.createElement('button');
    btn.className = 'copy-btn';
    btn.type = 'button';
    btn.textContent = 'copy';
    btn.addEventListener('click', async () => {
      const text = code.innerText;
      try {
        await navigator.clipboard.writeText(text);
      } catch {
        // fallback for non-secure contexts (file://): use legacy execCommand
        const t = document.createElement('textarea');
        t.value = text;
        document.body.appendChild(t);
        t.select();
        try { document.execCommand('copy'); } catch { /* noop */ }
        document.body.removeChild(t);
      }
      btn.textContent = 'copied';
      btn.classList.add('copied');
      setTimeout(() => {
        btn.textContent = 'copy';
        btn.classList.remove('copied');
      }, 1200);
    });
    pre.appendChild(btn);
  });

  // 3. Tiny C#-ish syntax highlighter for <pre><code class="lang-cs"> blocks.
  // Tokenizes comments first, then strings, then keywords / types / numbers.
  const KEYWORDS = new Set([
    'abstract', 'async', 'await', 'break', 'case', 'catch', 'class', 'const',
    'continue', 'default', 'delegate', 'do', 'else', 'enum', 'event', 'extern',
    'finally', 'for', 'foreach', 'get', 'if', 'in', 'init', 'interface',
    'internal', 'is', 'lock', 'namespace', 'new', 'null', 'object', 'operator',
    'out', 'override', 'params', 'private', 'protected', 'public', 'readonly',
    'record', 'ref', 'return', 'sealed', 'set', 'static', 'string', 'struct',
    'switch', 'this', 'throw', 'true', 'false', 'try', 'typeof', 'using',
    'value', 'var', 'virtual', 'void', 'where', 'while', 'with', 'yield',
    'partial', 'global', 'required'
  ]);

  function escapeHtml(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  function highlightCs(src) {
    // Match line/block comments, strings, numbers, identifiers, in one pass.
    const re = /(\/\/[^\n]*|\/\*[\s\S]*?\*\/)|("(?:\\.|[^"\\])*"|@"(?:[^"]|"")*"|\$"(?:\\.|[^"\\])*")|(\b\d+(?:\.\d+)?[mdfL]?\b)|([A-Za-z_][A-Za-z_0-9]*)/g;
    let out = '';
    let last = 0;
    let m;
    while ((m = re.exec(src)) !== null) {
      out += escapeHtml(src.slice(last, m.index));
      if (m[1]) out += '<span class="tok-com">' + escapeHtml(m[1]) + '</span>';
      else if (m[2]) out += '<span class="tok-str">' + escapeHtml(m[2]) + '</span>';
      else if (m[3]) out += '<span class="tok-num">' + escapeHtml(m[3]) + '</span>';
      else if (m[4]) {
        if (KEYWORDS.has(m[4])) out += '<span class="tok-kw">' + m[4] + '</span>';
        else if (/^[A-Z]/.test(m[4])) out += '<span class="tok-type">' + m[4] + '</span>';
        else out += m[4];
      }
      last = m.index + m[0].length;
    }
    out += escapeHtml(src.slice(last));
    return out;
  }

  document.querySelectorAll('pre > code.lang-cs').forEach((c) => {
    const text = c.textContent;
    c.innerHTML = highlightCs(text);
  });

  // 4. Heading anchor-links (hover to reveal #).
  document.querySelectorAll('main h2[id], main h3[id]').forEach((h) => {
    const anchor = document.createElement('a');
    anchor.href = '#' + h.id;
    anchor.textContent = ' #';
    anchor.style.opacity = '0';
    anchor.style.marginLeft = '8px';
    anchor.style.fontWeight = '400';
    anchor.style.color = 'var(--accent)';
    anchor.style.transition = 'opacity 0.15s';
    h.addEventListener('mouseenter', () => { anchor.style.opacity = '1'; });
    h.addEventListener('mouseleave', () => { anchor.style.opacity = '0'; });
    h.appendChild(anchor);
  });
})();
