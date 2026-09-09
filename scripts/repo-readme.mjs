// repo-readme.mjs — the repository README linter, identical in every Hawkynt repo.
// Consumed through the composite action; never copied into a repo.
//
// Where package-readme.cs governs the README that ships INSIDE a NuGet package, this governs the
// one at the repository root: the frame (title, badges, pitch), the canonical section order, and
// the emoji vocabulary. Sections shared with the package convention carry the identical name and
// emoji on both sides — see repo-readme/README.md, which is the single home of both tables.
//
// It is a text lint: no build, no SDK, no assembly. That is why it is Node and not C#, and why it
// can afford to run in the fast tier.
//
// Usage:
//   node scripts/repo-readme.mjs --check [root]    lint, fail on a blocking finding
//   node scripts/repo-readme.mjs --write [root]    normalize the badge block in place
//   node scripts/repo-readme.mjs --self-test       run the built-in suite
//
// Options:
//   --repo <owner/name>   slug the badges and the H1 are checked against. Defaults to
//                         $GITHUB_REPOSITORY, then to the basename of the root.
//   --gui                 the repo ships a GUI: a hero image and "## 🖼️ Screenshots" become required
//   --allow <rule[:arg]>  waive one rule, or one argument of it (repeatable). `--allow
//                         'heading-missing:🛠️ Building'` waives that one section, not all of them.
//   --license <spdx>      expected licence id (default: LGPL-3.0-or-later)
//   --sponsor <name>      GitHub Sponsors account. Default: read from .github/FUNDING.yml
//   --paypal <name>       paypal.me account.     Default: read from .github/FUNDING.yml
//   --verbose
//
// Emits ::error / ::warning workflow annotations when $GITHUB_ACTIONS is set, so findings land on
// the pull-request diff at the line they belong to.
//
// Exit: 0 success (advisories may have printed) · 1 blocking finding · 2 bad usage or environment.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// =============================================================================
//  The convention
// =============================================================================

/**
 * The canonical sections, in the order they must appear. `rank` is what the order rule compares;
 * every heading that is not one of these takes FREE_RANK, which is how "project-specific sections
 * live in one band in the middle" is expressed as a single non-decreasing check.
 *
 * `pkg: true` marks a section shared with the package README convention
 * (package-readme/README.md). Those MUST keep the same name and emoji on both sides — that is the
 * whole point of having one vocabulary. Changing one without the other is the drift this exists to
 * prevent.
 */
export const SECTIONS = [
  { rank:  1, emoji: '🧭', title: 'Vision',        required: 'always' },
  { rank:  2, emoji: '✨', title: 'Features',      required: 'always', pkg: true },
  { rank:  3, emoji: '🧩', title: 'Support matrix', required: 'never', pkg: true },
  { rank:  4, emoji: '📦', title: 'Installation',  required: 'always', pkg: true },
  { rank:  5, emoji: '🚀', title: 'Quick start',   required: 'always', pkg: true },
  { rank:  6, emoji: '🖼️', title: 'Screenshots',   required: 'gui' },
  // rank 7 — the free band. Project-specific sections, any order.
  { rank:  8, emoji: '🏗️', title: 'Architecture',  required: 'never', pkg: true },
  { rank:  9, emoji: '🔌', title: 'Dependencies',  required: 'never', pkg: true },
  { rank: 10, emoji: '⚠️', title: 'Limitations',   required: 'never', pkg: true },
  { rank: 11, emoji: '🛠️', title: 'Building',      required: 'always' },
  { rank: 12, emoji: '🤝', title: 'Contributing',  required: 'never' },
  { rank: 13, emoji: '🆘', title: 'Getting Help',  required: 'never' },
  { rank: 14, emoji: '❤️', title: 'Support',       required: 'always', pkg: true },
  { rank: 15, emoji: '📜', title: 'License',       required: 'always', pkg: true },
];

export const FREE_RANK = 7;

/**
 * Titles observed in the corpus that mean a canonical section under another name. They are renamed
 * rather than accepted: two names for one section is exactly the drift being removed. Matched on
 * the lowercased, emoji-stripped title.
 */
export const ALIASES = new Map(Object.entries({
  'install':                    'Installation',
  'installing':                 'Installation',
  'setup':                      'Installation',
  'getting started':            'Installation',
  'installation & usage':       'Installation',
  'installation and usage':     'Installation',
  'usage':                      'Quick start',
  'quickstart':                 'Quick start',
  'usage examples':             'Quick start',
  'screenshot':                 'Screenshots',
  'build':                      'Building',
  'build from source':          'Building',
  'build instructions':         'Building',
  'building and testing':       'Building',
  'contribute':                 'Contributing',
  'getting help':               'Getting Help',
  'help':                       'Getting Help',
  'known issues':               'Limitations',
  'known limitations':          'Limitations',
  'known bugs and limitations': 'Limitations',
  'disclaimer':                 'Limitations',
}));

/**
 * Emoji for the recurring free-band concepts. These are NOT rank-bearing: a free-band section may
 * be called anything, and this table only says which emoji to reach for so the same idea looks the
 * same across repos. Only the SECTIONS emoji above are enforced.
 */
export const FREE_VOCABULARY = Object.freeze({
  '📚': 'Documentation',
  '⚙️': 'How it works / Configuration',
  '📁': 'Project structure / Layout',
  '🧪': 'Testing',
  '📈': 'Performance',
  '🛡️': 'Security',
  '📋': 'Status',
  '🗺️': 'Roadmap',
  '💡': 'Inspiration / Prior art',
  '🤖': 'CI',
  '💻': 'Platform support',
});

// The badge block, in four groups. Byte-identical modulo the slug in most of the corpus, which is
// what makes --write possible at all.
export const BADGE_GROUPS = [
  ['License', 'Language'],
  ['CI', 'Last Commit', 'Activity'],
  ['Stars', 'Forks', 'Issues', 'Code Size', 'Repo Size'],
  ['Release', 'Nightly', 'Downloads'],
];

// Only these two are blocking. Everything else is legitimately absent somewhere: a repo that ships
// via a moving tag has no releases, so a Release badge would read "no releases" forever.
export const BADGES_REQUIRED = ['license', 'ci'];

export const BADGE_TEMPLATES = {
  'License':     r => `[![License](https://img.shields.io/github/license/${r})](https://github.com/${r}/blob/main/LICENSE)`,
  'Language':    r => `[![Language](https://img.shields.io/github/languages/top/${r}?color=8957D5)](https://github.com/${r})`,
  'CI':          r => `[![CI](https://github.com/${r}/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/${r}/actions/workflows/ci.yml)`,
  'Last Commit': r => `![Last Commit](https://img.shields.io/github/last-commit/${r}?branch=main)`,
  'Activity':    r => `![Activity](https://img.shields.io/github/commit-activity/m/${r})`,
  'Stars':       r => `[![Stars](https://img.shields.io/github/stars/${r}?color=FFD700)](https://github.com/${r}/stargazers)`,
  'Forks':       r => `[![Forks](https://img.shields.io/github/forks/${r}?color=008080)](https://github.com/${r}/network/members)`,
  'Issues':      r => `[![Issues](https://img.shields.io/github/issues/${r})](https://github.com/${r}/issues)`,
  'Code Size':   r => `![Code Size](https://img.shields.io/github/languages/code-size/${r}?color=4CAF50)`,
  'Repo Size':   r => `![Repo Size](https://img.shields.io/github/repo-size/${r}?color=FF9800)`,
  'Release':     r => `[![Release](https://img.shields.io/github/v/release/${r})](https://github.com/${r}/releases/latest)`,
  'Nightly':     r => `[![Nightly](https://img.shields.io/github/v/release/${r}?include_prereleases&sort=date&filter=nightly-*&label=nightly&color=FF9800)](https://github.com/${r}/releases)`,
  'Downloads':   r => `[![Downloads](https://img.shields.io/github/downloads/${r}/total)](https://github.com/${r}/releases)`,
};

export const SUPPORT_BODY = (sponsor, paypal) => [
  'If this project saves you time or money, consider supporting its development:',
  '',
  `[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/${sponsor})`,
  `[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/${paypal})`,
];

export const LICENSE_BODY = spdx => [`Licensed under ${spdx} — see [LICENSE](LICENSE).`];

// =============================================================================
//  Pure helpers — exported so the self-test can drive them directly
// =============================================================================

/**
 * Blanks fenced blocks and inline code spans so the link and placeholder checks do not read code as
 * markdown. Borrowed from Linter.StripCode in package-readme.cs, for the reason its comment gives: an
 * array-typed conversion operator renders as `operator TItem[](…)`, whose `[](` is indistinguishable
 * from a link to a regular expression.
 *
 * Fenced lines are BLANKED, not removed. Deleting them renumbers everything below the first code
 * block, and a finding that points at the wrong line is worse than no finding — the annotation would
 * land on innocent prose.
 */
export function stripCode(markdown) {
  const lines = markdown.split('\n');
  const fenced = fenceMask(lines);
  return lines.map((l, i) => (fenced[i] ? '' : l.replace(/`[^`\n]*`/g, ''))).join('\n');
}

const EMOJI_HEAD = /^((?:\p{Extended_Pictographic}|\p{Emoji_Presentation})(?:️|‍\p{Extended_Pictographic}|\p{Emoji_Modifier})*)\s*/u;

/** Splits "🖼️ Screenshots" into { emoji: '🖼️', title: 'Screenshots' }. */
export function splitEmoji(heading) {
  const m = EMOJI_HEAD.exec(heading);
  if (!m) return { emoji: '', title: heading.trim() };
  return { emoji: m[1], title: heading.slice(m[0].length).trim() };
}

export function normalizeTitle(title) {
  return title.trim().toLowerCase().replace(/\s+/g, ' ').replace(/[.:]+$/, '');
}

/** The canonical section a heading resolves to, by exact title or by alias. Null for the free band. */
export function sectionOf(headingText) {
  const { title } = splitEmoji(headingText);
  const key = normalizeTitle(title);
  const direct = SECTIONS.find(s => normalizeTitle(s.title) === key);
  if (direct) return direct;

  const aliased = ALIASES.get(key);
  return aliased ? SECTIONS.find(s => s.title === aliased) ?? null : null;
}

export function rankOf(headingText) {
  return sectionOf(headingText)?.rank ?? FREE_RANK;
}

/** Index of the first rank that is lower than one before it, or -1 when the sequence is sound. */
export function checkOrder(ranks) {
  for (let i = 1; i < ranks.length; ++i)
    if (ranks[i] < ranks[i - 1]) return i;

  return -1;
}

/**
 * Which lines sit inside a fenced code block. Every structural rule consults this first: a shell
 * example is full of `# clone the repository`, and reading those as H1 titles reported a second
 * title in four of the eight repositories this was first calibrated against.
 */
export function fenceMask(lines) {
  const mask = new Array(lines.length).fill(false);
  let open = false;
  for (let i = 0; i < lines.length; ++i) {
    if (/^\s*(```|~~~)/.test(lines[i])) { mask[i] = true; open = !open; continue; }
    mask[i] = open;
  }
  return mask;
}

/** Every `## ` heading with its 1-based line number and its resolved section. */
export function parseHeadings(lines) {
  const fenced = fenceMask(lines);
  const out = [];
  for (let i = 0; i < lines.length; ++i) {
    if (fenced[i] || !lines[i].startsWith('## ')) continue;

    const text = lines[i].slice(3).trim();
    out.push({ line: i + 1, text, ...splitEmoji(text), section: sectionOf(text), rank: rankOf(text) });
  }
  return out;
}

/**
 * The badge region: everything between the H1 and whatever ends it — the pitch, the first section,
 * or the hero image. Returns the badge lines found there plus the region bounds.
 */
export function parseBadgeRegion(lines) {
  const fenced = fenceMask(lines);
  const h1 = lines.findIndex((l, i) => !fenced[i] && l.startsWith('# '));
  if (h1 < 0) return { h1: -1, start: -1, end: -1, badges: [] };

  let end = lines.length;
  for (let i = h1 + 1; i < lines.length; ++i) {
    const l = lines[i];
    if (l.startsWith('## ') || l.trimStart().startsWith('>')) { end = i; break; }
    // A bare image line that is NOT a badge ends the region — that is the hero shot.
    if (/^\s*!\[[^\]]*\]\([^)]*\)\s*$/.test(l) && !/img\.shields\.io|badge\.svg/.test(l)) { end = i; break; }
  }

  const badges = [];
  for (let i = h1 + 1; i < end; ++i) {
    const m = /!\[([^\]]*)\]\(/.exec(lines[i]);
    if (m) badges.push({ line: i + 1, index: i, label: m[1].trim(), text: lines[i] });
  }

  return { h1, start: h1 + 1, end, badges };
}

export function badgeGroupOf(label) {
  const key = normalizeTitle(label);
  for (let g = 0; g < BADGE_GROUPS.length; ++g)
    if (BADGE_GROUPS[g].some(b => normalizeTitle(b) === key)) return g + 1;

  return 0; // unrecognized — language-specific, NuGet, whatever the repo needs
}

/** Reads the sponsor and paypal handles out of .github/FUNDING.yml, so the README and the Sponsor
 *  button cannot disagree. Deliberately a two-key scrape rather than a YAML dependency. */
export function parseFunding(text) {
  const out = {};
  const gh = /^\s*github:\s*\[?\s*"?([\w.-]+)"?/m.exec(text);
  if (gh) out.sponsor = gh[1];

  const custom = /paypal\.me\/([\w.-]+)/i.exec(text);
  if (custom) out.paypal = custom[1];

  return out;
}

/** Index of the first real `## ` heading, or lines.length when there is none. */
export function firstSectionIndex(lines) {
  const fenced = fenceMask(lines);
  const i = lines.findIndex((l, n) => !fenced[n] && l.startsWith('## '));
  return i < 0 ? lines.length : i;
}

/** The lines of a section's body: everything after its heading up to the next `## `. */
export function sectionBody(lines, headingLine) {
  const fenced = fenceMask(lines);
  const body = [];
  for (let i = headingLine; i < lines.length; ++i) {
    if (!fenced[i] && lines[i].startsWith('## ')) break;
    body.push(lines[i]);
  }
  while (body.length && body[0].trim() === '') body.shift();
  while (body.length && body[body.length - 1].trim() === '') body.pop();
  return body;
}

// =============================================================================
//  Findings
// =============================================================================

const BLOCK = 'error';
const ADVISE = 'warning';

class Report {
  constructor(allow) {
    this.findings = [];
    this.allow = allow;
  }

  add(severity, rule, line, message, arg) {
    if (this.waived(rule, arg)) return;
    this.findings.push({ severity, rule, line, message, arg });
  }

  error(rule, line, message, arg) { this.add(BLOCK, rule, line, message, arg); }
  warn(rule, line, message, arg) { this.add(ADVISE, rule, line, message, arg); }

  waived(rule, arg) {
    if (this.allow.has(rule)) return true;
    return arg != null && this.allow.has(`${rule}:${arg}`);
  }

  get blocking() { return this.findings.filter(f => f.severity === BLOCK); }
}

// =============================================================================
//  The rules
// =============================================================================

export function check(readme, ctx) {
  const report = new Report(ctx.allow ?? new Set());
  const lines = readme.replace(/\r\n/g, '\n').split('\n');
  const [, repoName] = ctx.repo.split('/');

  checkTitle(lines, ctx, report, repoName);
  const region = parseBadgeRegion(lines);
  checkBadges(lines, region, ctx, report);
  const pitchEnd = checkPitch(lines, region, report);
  checkHero(lines, pitchEnd, ctx, report);

  const headings = parseHeadings(lines);
  checkHeadings(lines, headings, ctx, report);
  checkVocabulary(headings, report);
  checkBodies(lines, headings, ctx, report);
  checkLinks(readme, ctx, report);

  return report;
}

function checkTitle(lines, ctx, report, repoName) {
  const fenced = fenceMask(lines);
  const indices = lines.map((l, i) => (!fenced[i] && l.startsWith('# ') ? i : -1)).filter(i => i >= 0);
  if (indices.length === 0) {
    report.error('h1-missing', 1, 'no H1 title.');
    return;
  }

  if (indices.length > 1)
    report.error('h1-multiple', indices[1] + 1,
      `two H1 titles (lines ${indices[0] + 1} and ${indices[1] + 1}); a README has exactly one.`);

  const first = lines.findIndex(l => l.trim() !== '' && !l.trimStart().startsWith('<!--'));
  if (first >= 0 && first !== indices[0])
    report.error('h1-not-first', first + 1, `'${lines[first].trim()}' appears above the H1.`);

  // Advisory, not blocking: '# 2D Image Filter' and '# 🗂️ DriveBenderUtility' are deliberate display
  // titles. The package rule is strict because nuget.org derives its page title from the H1; a repo
  // title is only ever read by a human.
  const shown = splitEmoji(lines[indices[0]].slice(2).trim()).title;
  const squash = s => s.toLowerCase().replace(/[^a-z0-9]/g, '');
  if (squash(shown) !== squash(repoName))
    report.warn('h1-repo-name', indices[0] + 1, `H1 is '${shown}', repository is '${repoName}'.`);
}

function checkBadges(lines, region, ctx, report) {
  if (region.h1 < 0) return;

  if (region.badges.length === 0) {
    report.error('badge-block-missing', region.start + 1, 'no badge block under the title.');
    return;
  }

  const labels = new Set(region.badges.map(b => normalizeTitle(b.label)));
  for (const required of BADGES_REQUIRED)
    if (!labels.has(required))
      report.error('badge-required', region.badges[0].line, `the badge block has no ${required} badge.`, required);

  // The copy-paste defect: a repo generated from the template whose badges still point at
  // RepositoryTemplate, or a renamed repo whose badges now 404. Region-scoped, so prose links to
  // other repositories are none of this rule's business.
  for (const b of region.badges) {
    if (!/github\.com\/|img\.shields\.io\/github\//.test(b.text)) continue;
    if (b.text.includes(`/${ctx.repo}`)) continue;

    report.error('badge-repo-mismatch', b.line,
      `the ${b.label || 'unnamed'} badge does not point at ${ctx.repo}.`);
  }

  const missing = BADGE_GROUPS.flat().filter(b => !labels.has(normalizeTitle(b)));
  if (missing.length)
    report.warn('badge-recommended', region.badges[0].line,
      `the badge block is missing ${missing.join(', ')}.`);

  // Order of first appearance, not blank-line grouping: some repos merge two groups into one block
  // and some omit a group entirely, and neither is a defect.
  const seen = [];
  for (const b of region.badges) {
    const g = badgeGroupOf(b.label);
    if (g === 0) continue;
    if (!seen.some(s => s.group === g)) seen.push({ group: g, badge: b });
  }
  const wrong = checkOrder(seen.map(s => s.group));
  if (wrong >= 0)
    report.warn('badge-group-order', seen[wrong].badge.line,
      `the ${seen[wrong].badge.label} badge sits before a group that belongs above it.`);
}

function checkPitch(lines, region, report) {
  const limit = firstSectionIndex(lines);
  const start = lines.findIndex((l, i) => i >= region.end && l.trimStart().startsWith('>'));

  if (start < 0 || start >= limit) {
    report.error('pitch-missing', Math.max(region.end, 1),
      "no '>' blockquote under the badges saying what this is and why someone would want it.");
    return region.end;
  }

  let end = start;
  while (end < limit && lines[end].trimStart().startsWith('>')) ++end;

  const quoted = lines.slice(start, end).map(l => l.replace(/^\s*>\s?/, ''));
  if (quoted.some(l => l.trim() === ''))
    report.warn('pitch-multi-paragraph', start + 1, 'the pitch runs to more than one paragraph.');

  const length = quoted.join(' ').trim().length;
  if (length > 450)
    report.warn('pitch-too-long', start + 1, `the pitch is ${length} characters; it is the hook, not the manual.`);

  return end;
}

function checkHero(lines, pitchEnd, ctx, report) {
  const limit = firstSectionIndex(lines);
  const images = [];
  for (let i = pitchEnd; i < limit; ++i)
    if (/!\[[^\]]*\]\([^)]*\)/.test(lines[i]) && !/img\.shields\.io|badge\.svg/.test(lines[i]))
      images.push(i + 1);

  // Only GUI repos are *required* to carry one. Any repo may: a logo, a diagram, a sample of the
  // output are all legitimate hooks, and flagging them taught nobody anything.
  if (!ctx.gui) return;

  if (images.length === 0)
    report.error('hero-missing', limit,
      'a GUI repo shows one image under the pitch — the shot that makes a reader want the rest.');
  else if (images.length > 1)
    report.error('hero-multiple', images[1],
      `${images.length} images under the pitch; exactly one is the hook. The rest belong in '## 🖼️ Screenshots'.`);
}

function checkHeadings(lines, headings, ctx, report) {
  // --- Present.
  for (const s of SECTIONS) {
    const need = s.required === 'always' || (s.required === 'gui' && ctx.gui);
    if (!need) continue;
    if (headings.some(h => h.section === s)) continue;

    const heading = `${s.emoji} ${s.title}`;
    report.error('heading-missing', 1, `missing required section '## ${heading}'.`, heading);
  }

  // --- Named and decorated as the vocabulary says.
  for (const h of headings) {
    if (!h.section) continue;

    const canonical = `## ${h.section.emoji} ${h.section.title}`;
    if (normalizeTitle(h.title) !== normalizeTitle(h.section.title)) {
      report.error('heading-name', h.line, `'## ${h.text}' is the ${h.section.title} section; call it '${canonical}'.`);
      h.flagged = true;
    } else if (h.emoji !== h.section.emoji) {
      report.error('heading-emoji', h.line, `'## ${h.text}' must be '${canonical}'.`);
      h.flagged = true;
    }
  }

  // --- Ordered. One non-decreasing check over ALL headings encodes both "the canonical sections
  //     come in this order" and "project-specific sections live in the band in the middle".
  const wrong = checkOrder(headings.map(h => h.rank));
  if (wrong >= 0)
    report.error('heading-order', headings[wrong].line,
      `'## ${headings[wrong].text}' comes after '## ${headings[wrong - 1].text}', which belongs below it.`);

  // --- Said once.
  for (let i = 0; i < headings.length; ++i) {
    for (let j = i + 1; j < headings.length; ++j) {
      const a = headings[i], b = headings[j];
      const sameTitle = normalizeTitle(a.title) === normalizeTitle(b.title);
      const sameSection = a.section && a.section === b.section;
      if (!sameTitle && !sameSection) continue;

      report.error('heading-duplicate', b.line,
        `'## ${b.text}' repeats '## ${a.text}' from line ${a.line}.`);
    }
  }

  // --- License closes the file. Implied by the order rule, but the fix is different and the
  //     message should say which one it is.
  const last = headings[headings.length - 1];
  if (last && last.section?.title !== 'License')
    report.error('license-not-last', last.line,
      `'## ${last.text}' comes after the licence; '## 📜 License' is the last section.`);

  if (ctx.gui) {
    const shots = headings.find(h => h.section?.title === 'Screenshots');
    if (shots && !sectionBody(lines, shots.line).some(l => /!\[[^\]]*\]\([^)]*\)/.test(l)))
      report.error('screenshots-empty', shots.line, 'the Screenshots section contains no image.');
  }
}

function checkVocabulary(headings, report) {
  const mapped = new Map(SECTIONS.map(s => [s.emoji, s]));

  for (const h of headings) {
    if (h.flagged || !h.emoji) continue;

    const owner = mapped.get(h.emoji);
    if (owner && owner !== h.section)
      report.error('emoji-reused-mapped', h.line,
        `'## ${h.text}' reuses ${h.emoji}, which means ${owner.title}. Pick an unmapped emoji.`);
  }

  const free = headings.filter(h => !h.section && h.emoji);
  const bare = headings.filter(h => !h.section && !h.emoji);
  if (bare.length)
    report.warn('heading-no-emoji', bare[0].line,
      `${bare.length} section(s) carry no emoji: ${bare.map(h => `'${h.text}'`).join(', ')}.`);

  const byEmoji = new Map();
  for (const h of free) {
    const prior = byEmoji.get(h.emoji);
    if (prior && normalizeTitle(prior.title) !== normalizeTitle(h.title))
      report.warn('emoji-duplicate-free', h.line,
        `${h.emoji} heads both '${prior.text}' and '${h.text}'; one emoji, one meaning.`);
    else if (!prior) byEmoji.set(h.emoji, h);
  }
}

function checkBodies(lines, headings, ctx, report) {
  const compare = (name, rule, expected) => {
    const h = headings.find(x => x.section?.title === name);
    if (!h) return;

    const actual = sectionBody(lines, h.line).map(l => l.replace(/\s+$/, ''));
    if (actual.join('\n') === expected.join('\n')) return;

    report.error(rule, h.line,
      `the ${name} section differs from the house text.\n${diff(expected, actual)}`);
  };

  compare('Support', 'support-body', SUPPORT_BODY(ctx.sponsor, ctx.paypal));
  compare('License', 'license-body', LICENSE_BODY(ctx.license));

  if (!fs.existsSync(path.join(ctx.root, 'LICENSE')))
    report.error('license-file-missing', 1, 'the README links a LICENSE that is not in the repository root.');
}

function diff(expected, actual) {
  const out = [];
  const n = Math.max(expected.length, actual.length);
  for (let i = 0; i < n; ++i) {
    if (expected[i] === actual[i]) continue;
    if (expected[i] != null) out.push(`         want: ${expected[i]}`);
    if (actual[i] != null) out.push(`         got:  ${actual[i]}`);
  }
  return out.join('\n');
}

function checkLinks(readme, ctx, report) {
  const text = stripCode(readme.replace(/\r\n/g, '\n'));
  const lines = text.split('\n');
  // A badge's href has to be absolute — it is rendered by shields.io and clicked from anywhere — so
  // the badge region is not where the relative-link advice applies.
  const region = parseBadgeRegion(readme.replace(/\r\n/g, '\n').split('\n'));

  for (let i = 0; i < lines.length; ++i) {
    for (const m of lines[i].matchAll(/\[[^\]]*\]\(([^)\s]+)/g)) {
      const raw = m[1].trim();
      if (/^(#|https?:|mailto:|data:)/.test(raw)) continue;

      const target = decodeURIComponent(raw.split('#')[0]);
      if (target === '') continue;

      const resolved = path.resolve(ctx.root, target);
      // A target that climbs out of the repository is not a file reference at all: `../../releases/latest`
      // is resolved by github.com against the blob URL, and lands on the releases page. Checking it
      // against the filesystem would report a link that works.
      if (!resolved.startsWith(path.resolve(ctx.root) + path.sep)) continue;
      if (fs.existsSync(resolved)) continue;

      report.error('link-broken-relative', i + 1, `'${target}' does not exist in the repository.`, target);
    }

    // A repo README renders on github.com, where a relative link survives a fork and an absolute
    // blob URL does not. This is the opposite of the package rule, on purpose.
    if (i >= region.start && i < region.end) continue;

    const slug = ctx.repo.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    for (const m of lines[i].matchAll(new RegExp(`\\]\\(https://github\\.com/${slug}/blob/[^)]+\\)`, 'g')))
      report.warn('link-absolute-self', i + 1,
        `${m[0].slice(2, -1)} could be a relative link, which survives a fork and a rename.`);
  }

  // Against the stripped text: a README that *documents* the placeholder writes it as `ProjectName`
  // in a code span, and an unreplaced one never does.
  const placeholder = /\{\{[A-Z_]+\}\}|\b(?:ProjectName|RepoName|Package\.Id)\b/.exec(text);
  if (placeholder) {
    const line = text.slice(0, placeholder.index).split('\n').length;
    report.error('placeholder', line, `unreplaced template placeholder '${placeholder[0]}'.`);
  }
}

// =============================================================================
//  --write: normalize the badge block
// =============================================================================

/**
 * Rewrites the badge region: every badge that is already there keeps its meaning but gets the
 * canonical slug and the canonical group position, and a missing REQUIRED badge is added.
 *
 * It deliberately does not add the recommended badges. A repo that ships from a moving tag has no
 * releases, and inventing a Release badge for it would be the tool making a claim the repo cannot
 * back. Unrecognized badges (language-specific, NuGet) are preserved, after the canonical ones.
 */
export function writeBadges(readme, repo) {
  const newline = readme.includes('\r\n') ? '\r\n' : '\n';
  const lines = readme.replace(/\r\n/g, '\n').split('\n');
  const region = parseBadgeRegion(lines);
  if (region.h1 < 0) return readme;

  const present = new Map();
  const extra = [];
  for (const b of region.badges) {
    const group = badgeGroupOf(b.label);
    if (group === 0) { extra.push(b.text); continue; }

    const canonical = BADGE_TEMPLATES[BADGE_GROUPS.flat().find(x => normalizeTitle(x) === normalizeTitle(b.label))];
    // Keep the repo's own variant when it already points at the right place — NativeForms and
    // ProcessManager use a different CI badge provider, and both are correct.
    present.set(normalizeTitle(b.label), b.text.includes(`/${repo}`) ? b.text : canonical(repo));
  }

  for (const required of BADGES_REQUIRED)
    if (!present.has(required)) {
      const label = BADGE_GROUPS.flat().find(x => normalizeTitle(x) === required);
      present.set(required, BADGE_TEMPLATES[label](repo));
    }

  const blocks = [];
  for (const group of BADGE_GROUPS) {
    const block = group.map(l => present.get(normalizeTitle(l))).filter(Boolean);
    if (block.length) blocks.push(block.join('\n'));
  }
  if (extra.length) blocks.push(extra.join('\n'));

  const rebuilt = [
    ...lines.slice(0, region.h1 + 1),
    '',
    ...blocks.join('\n\n').split('\n'),
    '',
    ...lines.slice(region.end),
  ];

  return rebuilt.join(newline);
}

// =============================================================================
//  Reporting
// =============================================================================

function print(report, file, verbose) {
  const annotate = !!process.env.GITHUB_ACTIONS;
  for (const f of report.findings) {
    const head = f.severity === BLOCK ? 'error' : 'warning';
    if (annotate)
      console.log(`::${head} file=${file},line=${f.line}::[${f.rule}] ${f.message.split('\n')[0]}`);

    console.log(`${file}:${f.line}: ${head}: [${f.rule}] ${f.message}`);
  }

  const blocking = report.blocking.length;
  const advisory = report.findings.length - blocking;
  if (report.findings.length === 0)
    console.log(`${file}: follows the house convention.`);
  else
    console.log(`${file}: ${blocking} blocking, ${advisory} advisory.`);

  if (verbose && report.allow.size)
    console.log(`waived: ${[...report.allow].join(', ')}`);

  return blocking > 0 ? 1 : 0;
}

// =============================================================================
//  Entry point
// =============================================================================

function fail(message) {
  console.error(`repo-readme: ${message}`);
  return 2;
}

export function run(argv) {
  let mode = null;
  let root = null;
  let repo = process.env.GITHUB_REPOSITORY ?? null;
  let gui = false;
  let license = 'LGPL-3.0-or-later';
  let sponsor = null;
  let paypal = null;
  let verbose = false;
  const allow = new Set();

  for (let i = 0; i < argv.length; ++i) {
    const a = argv[i];
    switch (a) {
      case '--check': case '--write': case '--self-test':
        if (mode) return fail(`--check, --write and --self-test are mutually exclusive (got both ${mode} and ${a}).`);
        mode = a;
        break;
      case '--repo':    if (++i >= argv.length) return fail('--repo needs a value.'); repo = argv[i]; break;
      case '--license': if (++i >= argv.length) return fail('--license needs a value.'); license = argv[i]; break;
      case '--sponsor': if (++i >= argv.length) return fail('--sponsor needs a value.'); sponsor = argv[i]; break;
      case '--paypal':  if (++i >= argv.length) return fail('--paypal needs a value.'); paypal = argv[i]; break;
      case '--allow':   if (++i >= argv.length) return fail('--allow needs a value.'); allow.add(argv[i].trim()); break;
      case '--gui':     gui = true; break;
      case '--verbose': verbose = true; break;
      default:
        if (a.startsWith('--')) return fail(`unknown option '${a}'.`);
        if (root) return fail(`more than one root given ('${root}' and '${a}').`);
        root = a;
    }
  }

  if (!mode) return fail('one of --check, --write or --self-test is required.');
  if (mode === '--self-test') return selfTest(verbose);

  root = path.resolve(root ?? '.');
  if (!fs.existsSync(root)) return fail(`'${root}' does not exist.`);

  const file = path.join(root, 'README.md');
  if (!fs.existsSync(file)) return fail(`'${file}' does not exist.`);

  if (!repo) repo = `Hawkynt/${path.basename(root)}`;
  if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) return fail(`--repo must be owner/name, got '${repo}'.`);

  const funding = readFunding(root);
  sponsor ??= funding.sponsor ?? repo.split('/')[0];
  paypal ??= funding.paypal ?? sponsor.toLowerCase();

  const readme = fs.readFileSync(file, 'utf8');

  if (mode === '--write') {
    const rewritten = writeBadges(readme, repo);
    if (rewritten === readme) {
      console.log('README.md: badge block already canonical.');
      return 0;
    }

    fs.writeFileSync(file, rewritten);
    console.log('README.md: badge block rewritten.');
    return 0;
  }

  const report = check(readme, { root, repo, gui, license, sponsor, paypal, allow });
  return print(report, 'README.md', verbose);
}

function readFunding(root) {
  const file = path.join(root, '.github', 'FUNDING.yml');
  if (!fs.existsSync(file)) return {};

  try { return parseFunding(fs.readFileSync(file, 'utf8')); }
  catch { return {}; }
}

// =============================================================================
//  Self-test
// =============================================================================

function selfTest(verbose) {
  let passed = 0, failed = 0;

  const ok = (name, condition) => {
    if (condition) { ++passed; return; }
    ++failed;
    console.log(`  FAIL ${name}`);
  };

  const rulesFor = (readme, ctx = {}) => {
    const report = check(readme, {
      root: path.join(FIXTURES, 'root'), repo: 'Hawkynt/Example', gui: false,
      license: 'LGPL-3.0-or-later', sponsor: 'Hawkynt', paypal: 'hawkynt',
      allow: new Set(), ...ctx,
    });
    return new Set(report.findings.filter(f => f.severity === BLOCK).map(f => f.rule));
  };

  console.log('repo-readme self-test');
  console.log();

  // --- Helpers -------------------------------------------------------------
  ok('splitEmoji separates a variation-selector emoji', splitEmoji('🖼️ Screenshots').title === 'Screenshots');
  ok('splitEmoji keeps a bare title', splitEmoji('License').emoji === '');
  ok('splitEmoji handles ❤️', splitEmoji('❤️ Support').emoji === '❤️');
  ok('sectionOf resolves an alias', sectionOf('🚀 Usage')?.title === 'Quick start');
  ok('sectionOf ignores an unknown title', sectionOf('🧱 What\'s in here') === null);
  ok('rankOf puts the unknown in the free band', rankOf('🧱 What\'s in here') === FREE_RANK);
  ok('checkOrder accepts a sound sequence', checkOrder([1, 2, 7, 7, 15]) === -1);
  ok('checkOrder finds the drop', checkOrder([1, 15, 7]) === 2);
  ok('stripCode blanks a fence', !stripCode('```\n[x](y)\n```').includes('[x](y)'));
  ok('stripCode blanks an inline span', stripCode('a `ProjectName` b') === 'a  b');
  // Line numbers must survive it, or every finding below the first code block points at the wrong
  // line and the annotation lands on innocent prose.
  ok('stripCode preserves the line count',
    stripCode('a\n```\nx\n```\nb').split('\n').length === 5);
  // A shell example is full of `# clone the repository`; reading those as titles reported a second
  // H1 in four of the first eight repositories this was calibrated against.
  ok('fenceMask covers a code block', fenceMask(['a', '```bash', '# comment', '```', 'b'])[2]);
  ok('parseHeadings ignores a fenced heading',
    parseHeadings(['## ✨ Features', '```', '## 📜 License', '```']).length === 1);
  ok('parseFunding reads both handles', (() => {
    const f = parseFunding('github: [Hawkynt]\ncustom: ["https://www.paypal.me/hawkynt"]');
    return f.sponsor === 'Hawkynt' && f.paypal === 'hawkynt';
  })());
  ok('badgeGroupOf places Release in group 4', badgeGroupOf('Release') === 4);
  ok('badgeGroupOf leaves NuGet unrecognized', badgeGroupOf('NuGet Core') === 0);

  // --- The fixtures --------------------------------------------------------
  const good = fs.readFileSync(path.join(FIXTURES, 'conforming.md'), 'utf8');
  ok('the conforming fixture is clean', rulesFor(good).size === 0);

  // Mutations of the good document, so each rule is proven to fire on exactly its own defect.
  ok('a bare ## License is caught', rulesFor(good.replace('## 📜 License', '## License')).has('heading-emoji'));
  ok('an aliased name is caught', rulesFor(good.replace('## 🚀 Quick start', '## 🚀 Usage')).has('heading-name'));
  ok('a reused mapped emoji is caught',
    rulesFor(good.replace('## 🧱 Layout', '## 🛠️ Layout')).has('emoji-reused-mapped'));
  ok('a missing section is caught', rulesFor(good.replace('## 🧭 Vision', '## 🧭 Ambition')).has('heading-missing'));
  ok('a waiver silences it', !rulesFor(good.replace('## 🧭 Vision', '## 🧭 Ambition'),
    { allow: new Set(['heading-missing:🧭 Vision']) }).has('heading-missing'));

  // Swapping two headings proves order is enforced and not merely presence.
  ok('swapped sections are caught', rulesFor(swap(good, '## ✨ Features', '## 📦 Installation')).has('heading-order'));
  // The template's own badges, left pointing at the template. The commonest real defect there is.
  ok('a badge for another repo is caught',
    rulesFor(good.replace(/^\[!\[License\].*$/m, BADGE_TEMPLATES['License']('Hawkynt/Other')))
      .has('badge-repo-mismatch'));
  ok('a missing pitch is caught', rulesFor(good.replace(/^> .*$/m, 'Not a blockquote.')).has('pitch-missing'));
  ok('a drifted Support body is caught',
    rulesFor(good.replace('consider supporting its development', 'consider chipping in')).has('support-body'));
  ok('a broken relative link is caught',
    rulesFor(good.replace('## 🧱 Layout', '## 🧱 Layout\n\nSee [the spec](SPEC.md).')).has('link-broken-relative'));
  // `../../releases/latest` is resolved by github.com against the blob URL and lands on the
  // releases page — a working link that a filesystem check would call broken.
  ok('a link that climbs out of the repo is left alone',
    !rulesFor(good.replace('## 🧱 Layout', '## 🧱 Layout\n\nThe [latest release](../../releases/latest).'))
      .has('link-broken-relative'));
  ok('a placeholder is caught', rulesFor(good.replace('Example', '{{REPO}}')).has('placeholder'));

  const gui = fs.readFileSync(path.join(FIXTURES, 'gui-no-shots.md'), 'utf8');
  ok('the GUI fixture is clean without --gui', rulesFor(gui).size === 0);
  ok('a GUI repo without a hero image is caught', rulesFor(gui, { gui: true }).has('hero-missing'));
  ok('a GUI repo without screenshots is caught', rulesFor(gui, { gui: true }).has('heading-missing'));

  // --- The golden file -----------------------------------------------------
  const defective = fs.readFileSync(path.join(FIXTURES, 'defective.md'), 'utf8');
  const report = check(defective, {
    root: path.join(FIXTURES, 'root'), repo: 'Hawkynt/Example', gui: false,
    license: 'LGPL-3.0-or-later', sponsor: 'Hawkynt', paypal: 'hawkynt', allow: new Set(),
  });
  const actual = report.findings
    .filter(f => f.severity === BLOCK)
    .map(f => `${f.rule}\t${f.line}\t${f.arg ?? ''}`)
    .sort()
    .join('\n') + '\n';

  const golden = path.join(FIXTURES, 'EXPECTED.txt');
  if (!fs.existsSync(golden)) {
    fs.writeFileSync(golden, actual);
    console.log(`  WROTE golden file ${golden} — review it, then re-run.`);
  } else {
    const expected = fs.readFileSync(golden, 'utf8');
    ok('defective.md matches the golden file', expected === actual);
    if (expected !== actual && verbose) {
      console.log('--- expected ---'); console.log(expected);
      console.log('--- actual   ---'); console.log(actual);
    }
  }

  // --- --write is idempotent ----------------------------------------------
  const once = writeBadges(good, 'Hawkynt/Example');
  ok('writeBadges is idempotent', writeBadges(once, 'Hawkynt/Example') === once);
  ok('writeBadges leaves a canonical block alone', once === good);

  console.log();
  console.log(`${passed} passed, ${failed} failed.`);
  return failed > 0 ? 1 : 0;
}

function swap(text, a, b) {
  const mark = '<!--swap-->';
  return text.replace(a, mark).replace(b, a).replace(mark, b);
}

const FIXTURES = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'repo-readme');

// Skipped when imported as a module (for tests). pathToFileURL handles Windows separators AND
// percent-encodes blanks, so the comparison also holds for working copies in paths with spaces.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href)
  process.exit(run(process.argv.slice(2)));
