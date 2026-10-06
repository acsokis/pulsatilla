import { readFile, writeFile, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const launch = path.join(root, 'docs', 'launch');
const project = await readFile(path.join(root, 'Pulsatilla.Wpf', 'Pulsatilla.Wpf.csproj'), 'utf8');
const productVersion = project.match(/<Version>(\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?)<\/Version>/)?.[1];
const productInfo = await readFile(path.join(root, 'Pulsatilla.Wpf', 'ProductInfo.cs'), 'utf8');
const startedAt = productInfo.match(/DevelopmentStartedAt = "([^"]+)"/)?.[1];
if (!productVersion || !startedAt || !Number.isFinite(Date.parse(startedAt))) throw new Error('Missing product version or development start.');
const startedDate = new Date(startedAt);
const startFormat = { dateStyle: 'long', timeStyle: 'short', timeZone: 'Europe/Berlin' };
const startedEnglish = new Intl.DateTimeFormat('en-GB', startFormat).format(startedDate);
const startedGerman = new Intl.DateTimeFormat('de-DE', startFormat).format(startedDate);
// Download links refer to the published snapshot, independently of the source-preview version.
const releasedVersion = '1.1.0-beta.1';
const releaseUrl = `https://github.com/acsokis/pulsatilla/releases/tag/v${releasedVersion}`;
const downloadUrl = `https://github.com/acsokis/pulsatilla/releases/download/v${releasedVersion}`;
const kitUrl = `${downloadUrl}/pulsatilla-publication-kit-${releasedVersion}.zip`;
const appUrl = `${downloadUrl}/pulsatilla-${releasedVersion}-win-x64.zip`;
const escape = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
const entries = [
  ['linkedin-en', 'en', 'LinkedIn post', 'linkedin-en.md'],
  ['facebook-en', 'en', 'Facebook post', 'facebook-en.md'],
  ['article-en', 'en', 'Long article · LinkedIn / Facebook', 'article-en.md'],
  ['linkedin-de', 'de', 'LinkedIn-Beitrag', 'linkedin-de.md'],
  ['facebook-de', 'de', 'Facebook-Beitrag', 'facebook-de.md'],
  ['article-de', 'de', 'Langer Artikel · LinkedIn / Facebook', 'article-de.md'],
];
const cards = [];
for (const [id, language, title, file] of entries) {
  const text = (await readFile(path.join(launch, file), 'utf8')).trim().replace(/^#\s+/, '');
  if (/Göldner|Goldner|netw_tool/i.test(text)) throw new Error(`Technical-origin attribution is not intended for copy-ready marketing: ${file}`);
  if (!text.includes('https://github.com/acsokis/pulsatilla') || !text.includes('https://paypal.me/gaborcarter'))
    throw new Error(`Missing project/support link in ${file}`);
  cards.push(`<article class="copy-card" data-language="${language}">
    <div class="card-heading"><h3>${title}</h3><span>${language.toUpperCase()}</span></div>
    <textarea id="${id}" spellcheck="false" readonly aria-label="${title}">${escape(text)}</textarea>
    <div class="actions"><button type="button" data-copy="${id}">${language === 'de' ? 'Text kopieren' : 'Copy text'}</button>
    <a class="secondary" href="${file}" download>${language === 'de' ? 'Textdatei herunterladen' : 'Download text file'}</a></div>
  </article>`);
}
const assets = [
  ['assets/marketing/pulsatilla-launch-en.png', 'Pulsatilla brand banner', 'AI-generated brand illustration · 1672 × 941', 'brand'],
  ['assets/marketing/pulsatilla-services-roadmap-en.png', 'Service roadmap · English', 'AI-generated illustration · planned services · 1254 × 1254', 'brand'],
  ['assets/marketing/pulsatilla-services-roadmap-de.png', 'Service roadmap · Deutsch', 'KI-Illustration · geplante Leistungen · 1672 × 941', 'brand'],
  ['assets/marketing/pulsatilla-logo.png', 'Pulsatilla logo', 'Brand mark and wordmark · 1280 × 320', 'brand'],
  ['assets/screenshots/community-beta2-dashboard-dark-en.png', 'Dashboard · English', 'Actual WPF interface · synthetic demo data · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-dashboard-dark-de.png', 'Dashboard · Deutsch', 'WPF-Oberfläche · synthetische Demodaten · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-network-path-dark-en.png', 'Network Path', 'Community beta.2 WPF interface · synthetic topology · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-live-traffic-dark-en.png', 'Application traffic', 'Actual WPF interface · synthetic demo data · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-security-sources-dark-en.png', 'IP and software activity', 'Actual WPF interface · synthetic demo data · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-email-review-dark-en.png', 'Local email review', 'Actual WPF interface · synthetic example message · 1500 × 900', 'screen'],
  ['assets/screenshots/community-beta2-dashboard-light-en.png', 'Dashboard · Light theme', 'Actual WPF interface · synthetic demo data · 1500 × 900', 'screen'],
];
for (const [file] of assets) await access(path.join(launch, file));
const images = assets.map(([file, title, caption, type]) => `<figure class="image-card ${type}">
  <a href="${file}" target="_blank" rel="noopener"><img src="${file}" alt="${escape(title + ' — ' + caption)}" loading="lazy"></a>
  <figcaption><strong>${title}</strong><p>${caption}</p><a href="${file}" download>Download PNG</a></figcaption>
</figure>`).join('\n');
const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Pulsatilla · Copy &amp; share · Gabor Web</title>
<meta name="description" content="English and German Pulsatilla posts, articles and images ready to copy and share.">
<style>
:root{color-scheme:dark;--bg:#080e0d;--panel:#111b18;--line:#294137;--text:#ecf3ef;--muted:#a6b9b0;--green:#90e0a9;--amber:#ffd077;--violet:#c6a5ff}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font-family:Segoe UI,Arial,sans-serif;line-height:1.6}
main{width:min(1240px,92%);margin:0 auto 64px}header{border-bottom:1px solid var(--line);padding:22px 0;margin-bottom:28px;display:flex;align-items:center;justify-content:space-between;gap:20px}
.brand{font-size:23px;font-weight:750;letter-spacing:.16em}.brand small{display:block;color:var(--green);font-size:10px;letter-spacing:.12em;font-weight:500}
a{color:var(--green);text-underline-offset:4px}nav{display:flex;gap:22px;flex-wrap:wrap}h1{font-size:clamp(30px,4vw,52px);line-height:1.14;letter-spacing:-.03em;margin:0 0 18px}h2{font-size:29px;margin:42px 0 10px}h3{font-size:19px;line-height:1.3;margin:0}
.hero{display:grid;grid-template-columns:.85fr 1.15fr;gap:32px;align-items:center}.hero img{width:100%;border-radius:14px;border:1px solid var(--line)}
.eyebrow{color:var(--amber);font-size:12px;letter-spacing:.14em;font-weight:700}.intro,.note{color:var(--muted)}.note{font-size:13px}.steps{border-left:3px solid var(--green);padding-left:16px;margin:24px 0}
.tabs{display:flex;gap:10px;margin:22px 0}.tabs button{background:transparent;color:var(--text);border:1px solid var(--line)}.tabs button.active{background:var(--green);color:#08100b}
button,.primary,.secondary{border:0;border-radius:7px;padding:11px 17px;font:600 14px Segoe UI,Arial,sans-serif;cursor:pointer;display:inline-block;text-decoration:none}button,.primary{background:var(--green);color:#08100b}button:hover,.primary:hover{filter:brightness(1.08)}button:focus-visible,a:focus-visible,textarea:focus-visible{outline:3px solid var(--amber);outline-offset:4px}
.secondary{background:#1b2a23;color:var(--text);border:1px solid var(--line)}.copy-grid,.image-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:22px}.copy-card,.image-card{background:var(--panel);border:1px solid var(--line);border-radius:12px;overflow:hidden}.copy-card{padding:22px}
.copy-card[hidden]{display:none}.card-heading{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:15px}.card-heading span{color:var(--violet);font-size:12px;font-weight:700}
textarea{width:100%;height:330px;resize:vertical;border:1px solid #33493e;background:#090f0c;color:var(--text);border-radius:6px;padding:16px;font:14px/1.65 Segoe UI,Arial,sans-serif}.actions{display:flex;gap:10px;flex-wrap:wrap;margin-top:15px}
figure{margin:0}.image-card img{width:100%;height:260px;object-fit:contain;background:#050907;display:block}.image-card figcaption{padding:18px}.image-card p{font-size:13px;color:var(--muted);margin:6px 0 12px}.image-card.brand img{object-fit:contain}
.strip{display:flex;gap:24px;flex-wrap:wrap;padding:18px 0;margin:20px 0;border-top:1px solid var(--line);border-bottom:1px solid var(--line);font-size:14px}.strip b{color:var(--amber)}
.service-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:22px}.service-card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:22px}.service-card p{color:var(--muted);font-size:14px}.service-card[hidden]{display:none}
#status{position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:var(--green);color:#08100b;padding:12px 24px;border-radius:8px;box-shadow:0 6px 30px #0008;max-width:90%;text-align:center;z-index:5}#status:empty{display:none}
footer{border-top:1px solid var(--line);margin-top:42px;padding-top:20px;color:var(--muted);font-size:13px}
@media(max-width:760px){header{align-items:flex-start;flex-wrap:wrap}.hero,.copy-grid,.image-grid,.service-grid{grid-template-columns:1fr}nav{gap:12px;font-size:13px}.hero img{order:-1}textarea{height:340px}.copy-card{padding:18px}.brand{font-size:18px}}
@media print{body{background:white;color:black}.copy-card{break-inside:avoid}button,.tabs,nav,#status{display:none}textarea{height:500px;color:black;background:white}.hero{display:block}}
</style></head><body><main>
<header><div class="brand">PULSATILLA<small>NETWORK CLARITY. SECURITY INSIGHT.</small></div><nav><a href="#copy">Posts &amp; articles</a><a href="#services">Services</a><a href="#images">Images</a><a href="https://github.com/acsokis/pulsatilla">GitHub</a></nav></header>
<section class="hero"><div><p class="eyebrow">GABOR WEB · ENGLISH + DEUTSCH · COMMUNITY ${productVersion}</p><h1>Make network activity visible.</h1><p class="intro">Your publication kit for Pulsatilla: four LinkedIn/Facebook posts, two longer articles, eleven PNG images and a reusable vector logo.</p><p class="note" data-language="en">Development started: <time datetime="${startedAt}">${startedEnglish} CEST</time> · Germany / Europe/Berlin.</p><p class="note" data-language="de" hidden>Entwicklungsbeginn: <time datetime="${startedAt}">${startedGerman} MESZ</time> · Deutschland / Europe/Berlin.</p><div class="actions"><a class="primary" href="${kitUrl}">Download complete kit (ZIP)</a><a class="secondary" href="${appUrl}">Download Windows app</a></div><p class="note"><a href="${releaseUrl}">${releasedVersion} published prerelease and checksums</a> · App requires .NET 8 Windows Desktop Runtime x64.</p><div class="steps">1. Choose a language and copy your text.<br>2. Download a PNG and attach it to your post.<br>3. Preview and publish in LinkedIn or Facebook.</div><p class="note">Extract the publication ZIP and open share.html locally to keep every image and text link available. No social post is sent by this page.</p></div><img src="assets/marketing/pulsatilla-launch-en.png" alt="Pulsatilla brand banner: a violet flower and amber/violet network streams"></section>
<div class="strip"><span><b>Community:</b> free, open source / MIT</span><span><b>Core:</b> separate private development</span><span><b>Support:</b> voluntary PayPal</span><span><b>Services:</b> planned</span></div>
<section id="copy"><h2>Copy a post or article</h2><p class="intro">Plain text, including project and PayPal links. The longer article can be used in either platform's article/post editor.</p><div class="tabs" aria-label="Publication language"><button type="button" class="active" data-language-tab="en" aria-pressed="true">English</button><button type="button" data-language-tab="de" aria-pressed="false">Deutsch</button></div><div class="copy-grid">${cards.join('\n')}</div></section>
<section id="services"><h2>Community, support &amp; service roadmap</h2><div class="service-grid">
<article class="service-card" data-language="en"><h3>MIT core &amp; voluntary support</h3><p>The Community app remains available without payment, account or activation. PayPal support helps development and does not unlock features or buy a service.</p><a href="https://paypal.me/gaborcarter">Support via PayPal</a></article>
<article class="service-card" data-language="en"><h3>Planned paid services</h3><p>Assisted setup, network diagnostics, training, support and custom integrations are the business direction. Discuss a scoped pilot with Gabor Web; prices, checkout and subscriptions are not active.</p><a href="https://www.linkedin.com/in/gabor-web/">Discuss a pilot on LinkedIn</a></article>
<article class="service-card" data-language="en"><h3>Later possibilities</h3><p>Managed reports and opt-in provider-backed exposure monitoring are future directions. Current exposure review uses locally imported reports; no external breach service or VPN is implemented.</p><a href="commercial-overview.md">Read the commercial overview</a></article>
<article class="service-card" data-language="de" hidden><h3>MIT-Kern &amp; freiwillige Unterstützung</h3><p>Die Community-App bleibt ohne Zahlung, Konto oder Aktivierung verfügbar. PayPal-Unterstützung hilft der Entwicklung; sie schaltet keine Funktionen frei und kauft keine Leistung.</p><a href="https://paypal.me/gaborcarter">Über PayPal unterstützen</a></article>
<article class="service-card" data-language="de" hidden><h3>Geplante kostenpflichtige Leistungen</h3><p>Einrichtung, Netzwerkdiagnose, Schulung, Support und individuelle Integrationen bilden die Geschäftsidee. Ein klar abgegrenztes Pilotprojekt lässt sich mit Gabor Web besprechen; Preise, Checkout und Abonnements sind noch nicht aktiv.</p><a href="https://www.linkedin.com/in/gabor-web/">Pilotprojekt auf LinkedIn besprechen</a></article>
<article class="service-card" data-language="de" hidden><h3>Spätere Möglichkeiten</h3><p>Verwaltete Berichte und freiwillige Datenleck-Prüfung über einen externen Anbieter sind spätere Entwicklungsrichtungen. Aktuell werden importierte Berichte lokal geprüft; ein externer Datenleck-Dienst oder VPN ist nicht implementiert.</p><a href="commercial-overview.md">Geschäftliche Übersicht lesen</a></article>
</div></section>
<section id="images"><h2>Attach an image</h2><p class="intro">Use a Dashboard screenshot to show how the application works. Use the brand banner or service-roadmap artwork when introducing Pulsatilla and future service opportunities.</p><p class="note">Screenshots show the current Community ${productVersion} source preview with synthetic demo data. Download links point to the earlier published ${releasedVersion} snapshot; obtain this updated kit from the current repository ZIP. The three flower/network banners are AI-generated illustrations. Service artwork describes a roadmap; no paid subscription or external breach service is active.</p><div class="image-grid">${images}</div><p><a href="assets/marketing/pulsatilla-logo.svg" download>Download vector logo (SVG)</a> · <a href="seo-geo.md">SEO / GEO copy and image captions</a></p></section>
<footer>Updated 2026-10-06 · Gábor Kocsis / Gabor Web · <a href="https://www.linkedin.com/in/gabor-web/">Contact on LinkedIn</a> · <a href="https://paypal.me/gaborcarter">Support via PayPal</a><br><a href="${kitUrl}">Published publication ZIP · ${releasedVersion}</a> · <a href="README.md">publication index</a> · <a href="COPY-NOTES.md">captions and copy notes</a></footer>
</main><div id="status" role="status" aria-live="polite"></div><script>
const status = document.getElementById('status');
let statusTimeout;
function announce(message) { status.textContent = message; clearTimeout(statusTimeout); statusTimeout = setTimeout(() => { status.textContent = ''; }, 6500); }
document.querySelectorAll('[data-language-tab]').forEach(button => button.addEventListener('click', () => {
  document.querySelectorAll('[data-language-tab]').forEach(tab => { const selected = tab === button; tab.classList.toggle('active', selected); tab.setAttribute('aria-pressed', String(selected)); });
  document.querySelectorAll('[data-language]').forEach(card => { card.hidden = card.dataset.language !== button.dataset.languageTab; });
}));
document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
  const field = document.getElementById(button.dataset.copy);
  try { await navigator.clipboard.writeText(field.value); announce('Copied. Paste the text into LinkedIn or Facebook.'); }
  catch { field.focus(); field.select(); const copied = document.execCommand('copy'); announce(copied ? 'Copied. Paste the text into LinkedIn or Facebook.' : 'Text selected. Press Ctrl+C / Cmd+C to copy it.'); }
}));
document.querySelector('[data-language-tab="en"]').click();
</script></body></html>\n`;
await writeFile(path.join(launch, 'share.html'), html, 'utf8');
console.log(`Built docs/launch/share.html: ${entries.length} copy-ready texts, ${assets.length} PNGs; all local assets verified.`);
