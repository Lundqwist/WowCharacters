// The crafting page (forever/crafting.html): my Forever characters' professions and the recipes they know.
// Skill levels and known recipes come from Blizzard's API through this app's /api/forever/crafting; the recipe
// lists are static files in recipes/<profession>.json (made from Wowhead's Forever database).

const ARMORY = 'https://worldofwarcraft.blizzard.com/en-gb/forever/eu/armory/character/'
const WOWHEAD = 'https://www.wowhead.com/forever/'
const ICONS = 'https://wow.zamimg.com/images/wow/icons/small/'
const QUALITY = ['#9d9d9d', '#ffffff', '#1eff00', '#3d9bff', '#c069ff', '#ff8000']
const SOURCES = { 2: 'drop', 4: 'quest', 5: 'vendor' }
const MAX_SKILL = 300

const status = document.getElementById('status')
const root = document.getElementById('crafting')

// Categories A-Z, with the catch-all "Other" last
const byCategory = (a, b) => (a === 'Other') - (b === 'Other') || a.localeCompare(b)
const icon = (name) => name && el('img', { class: 'wow-icon', src: `${ICONS}${name}.jpg`, alt: '', loading: 'lazy', width: 18, height: 18 })
const wowLink = (path, color, ...children) =>
  el('a', { href: WOWHEAD + path, target: '_blank', rel: 'noopener', style: `color:${color || '#fff'}` }, ...children)

// Each recipe file is fetched once, however many characters have the profession
const recipeFiles = new Map()
function recipeFile(name) {
  if (!recipeFiles.has(name)) {
    recipeFiles.set(name, getJson(`../recipes/${name}.json`).catch((e) => { recipeFiles.delete(name); throw e }))
  }
  return recipeFiles.get(name)
}

// Where the recipe is learned: the trainer, or the item that teaches it (a Wowhead link, with its tooltip)
function learnedFrom(r) {
  const where = r.src.map((s) => SOURCES[s]).filter(Boolean).join(' or ')
  if (r.src.length === 0 || r.src.includes(6)) return 'Trainer'
  if (r.recipe) return [wowLink(`item=${r.recipe[0]}`, QUALITY[r.recipe[2]], r.recipe[1]), where && ` (${where})`]
  return where ? `Recipe from ${where}` : 'Trainer'
}

function recipeRow(data, r, known) {
  const made = r.creates && data.items[r.creates[0]]
  const count = r.creates && (r.creates[1] === r.creates[2] ? r.creates[1] : `${r.creates[1]}-${r.creates[2]}`)
  return el('li', { class: known ? 'recipe' : 'recipe unknown' },
    el('div', { class: 'what' },
      wowLink(`spell=${r.id}`, QUALITY[made ? made[2] : 1], icon(r.icon || (made && made[1])), r.name),
      count && count !== 1 ? el('span', { class: 'muted', text: ` ×${count}` }) : null,
      el('div', { class: 'muted small' }, `Skill ${r.skill} · `, learnedFrom(r), known ? null : ' · not learned yet')),
    el('ul', { class: 'mats', 'aria-label': 'Materials' }, r.mats.map(([id, n]) => {
      const item = data.items[id] || ['Item ' + id, '', 1]
      return el('li', {}, `${n}× `, wowLink(`item=${id}`, QUALITY[item[2]], icon(item[1]), item[0]))
    })))
}

// The recipes of one profession for one character: what he knows, and on request what he doesn't yet
function recipeList(file, label, knownIds) {
  const body = el('div', { class: 'recipes-body' }, el('p', { class: 'muted', text: 'Loading recipes…' }))
  const details = el('details', { class: 'recipes' }, el('summary', { text: label }), body)
  details.addEventListener('toggle', async () => {
    if (!details.open || details.dataset.loaded) return
    details.dataset.loaded = '1'
    try {
      const data = await recipeFile(file)
      const recipes = data.recipes.map((r) => ({ r, known: knownIds.has(r.id) }))
      recipes.forEach((x) => { x.row = recipeRow(data, x.r, x.known); x.name = x.r.name.toLowerCase() })
      const cats = [...new Set(data.recipes.map((r) => r.cat || 'Other'))].sort(byCategory)

      const search = el('input', { type: 'search', placeholder: 'Search recipes', 'aria-label': `Search ${label}` })
      const category = el('select', { 'aria-label': 'Category' }, el('option', { value: '', text: 'All categories' }),
        cats.map((c) => el('option', { value: c, text: c })))
      const sort = el('select', { 'aria-label': 'Sort by' },
        el('option', { value: 'cat', text: 'Sort: category' }), el('option', { value: 'skill', text: 'Sort: skill' }), el('option', { value: 'name', text: 'Sort: name' }))
      const all = el('input', { type: 'checkbox' })
      const shown = el('span', { class: 'muted small', role: 'status' })
      const list = el('ul', { class: 'recipe-list' })

      const update = () => {
        const q = search.value.trim().toLowerCase()
        const visible = recipes.filter((x) => (all.checked || x.known) && x.name.includes(q)
          && (!category.value || (x.r.cat || 'Other') === category.value))
        const byName = (a, b) => a.r.name.localeCompare(b.r.name)
        const bySkill = (a, b) => a.r.skill - b.r.skill || byName(a, b)
        if (sort.value === 'cat') {
          visible.sort((a, b) => byCategory(a.r.cat || 'Other', b.r.cat || 'Other') || bySkill(a, b))
          const items = []
          let last
          for (const v of visible) {
            const c = v.r.cat || 'Other'
            if (c !== last) items.push(el('li', { class: 'recipe-group', text: c }))
            items.push(v.row)
            last = c
          }
          list.replaceChildren(...items)
        } else {
          list.replaceChildren(...visible.sort(sort.value === 'name' ? byName : bySkill).map((v) => v.row))
        }
        if (visible.length === 0) list.append(el('li', { class: 'muted', text: 'No recipes match.' }))
        const knownCount = recipes.filter((x) => x.known).length
        shown.textContent = `${visible.length} shown · knows ${knownCount} of ${recipes.length}`
      }
      search.addEventListener('input', update)
      for (const c of [category, sort, all]) c.addEventListener('change', update)
      body.replaceChildren(
        el('div', { class: 'recipe-tools' }, search, category, sort, el('label', { class: 'check' }, all, 'Also recipes not learned yet'), shown),
        el('div', { class: 'recipe-head', 'aria-hidden': 'true' }, el('span', { text: 'Recipe' }), el('span', { text: 'Materials' })),
        list)
      update()
    } catch {
      body.replaceChildren(el('p', { class: 'notice', text: 'The recipes could not be loaded. Try again in a little while.' }))
      details.dataset.loaded = ''
    }
  })
  return details
}

function professionCard(p, skill, knownIds) {
  const pct = Math.min(100, Math.round((skill / MAX_SKILL) * 100))
  return el('li', { class: 'panel profession' },
    el('div', { class: 'profession-head' },
      el('h3', { text: p.name }),
      el('span', { class: 'skill-value', text: `${skill} / ${MAX_SKILL}` })),
    el('div', { class: 'skill-bar', role: 'meter', 'aria-valuemin': 0, 'aria-valuemax': MAX_SKILL, 'aria-valuenow': skill, 'aria-label': `${p.name} skill` },
      el('span', { style: `width:${pct}%` })),
    p.recipes
      ? recipeList(p.recipes, `Recipes (${knownIds.size} known)`, knownIds)
      : el('p', { class: 'muted small gathering', text: 'Gathering: no recipes.' }),
    p.guide ? el('a', { class: 'guide-link small', href: p.guide, target: '_blank', rel: 'noopener', text: 'Leveling guide ↗' }) : null)
}

getJson('../api/forever/crafting')
  .then((data) => {
    title('Crafting (WoW: Forever)')
    if (!data.armory) {
      status.textContent = 'Armory information is not available right now, so there is no crafting information.'
      return
    }
    const sections = []
    const nav = document.getElementById('char-nav')

    for (const c of data.characters) {
      // No profile on the Armory (below level 10, or not read): nothing to show
      if (!c.professions) continue
      const name = c.card?.name || c.name
      const cls = c.card?.class
      const color = CLASS_COLORS[cls] || 'var(--gold)'
      const id = c.key

      nav.append(el('a', { href: `#${id}`, style: `--class:${color}`, text: name }))
      sections.push(el('section', { class: 'crafter-section', id, style: `--class:${color}` },
        el('h2', {},
          el('a', { class: 'crafter-name', href: `character.html?c=${encodeURIComponent(c.key)}`, text: name }),
          c.card ? el('span', { class: 'muted crafter-info', text: describe({ level: c.card.level, race: c.card.race, class: cls }) }) : null),
        c.professions.length
          ? el('ul', { class: 'profession-grid' }, c.professions.map((s) => {
              const p = data.professions.find((x) => x.name === s.name) || { name: s.name }
              return professionCard(p, s.skill, new Set(s.known))
            }))
          : el('p', { class: 'muted', text: 'No professions yet.' }),
        el('p', { class: 'small' }, el('a', { href: ARMORY + c.key, target: '_blank', rel: 'noopener', text: 'View on the Armory ↗' }))))
    }

    if (sections.length === 0) status.textContent = 'None of my Forever characters has professions on the Armory yet.'
    else status.remove()
    root.replaceChildren(...sections)
    if (location.hash) document.getElementById(decodeURIComponent(location.hash.slice(1)))?.scrollIntoView()
  })
  .catch(() => (status.textContent = 'The crafting information could not be loaded. Try again in a little while.'))
