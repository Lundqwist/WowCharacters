// Shared by every page. The pages live under /wowcharacters/ on lundqwist.se, so every
// address here is relative: the game pages are one folder down (retail/, forever/).

// The class colors used in the game
const CLASS_COLORS = {
  'Death Knight': '#c41e3a', 'Demon Hunter': '#a330c9', 'Druid': '#ff7c0a', 'Evoker': '#33937f',
  'Hunter': '#aad372', 'Mage': '#3fc7eb', 'Monk': '#00ff98', 'Paladin': '#f48cba', 'Priest': '#ffffff',
  'Rogue': '#fff468', 'Shaman': '#0070dd', 'Warlock': '#8788ee', 'Warrior': '#c69b6d',
}

// Builds an element: el('a', { href: '...', class: 'x', text: 'Hi' }, child, child)
function el(tag, attrs = {}, ...children) {
  const e = document.createElement(tag)
  for (const [k, v] of Object.entries(attrs)) {
    if (v === undefined || v === null || v === false) continue
    if (k === 'text') e.textContent = v
    else if (k === 'class') e.className = v
    else if (k === 'style') e.style.cssText = v
    else e.setAttribute(k, v)
  }
  for (const c of children.flat(Infinity)) if (c !== null && c !== undefined && c !== false) e.append(c)
  return e
}

// The game this page belongs to, from <body data-game="retail">
const GAME = document.body.dataset.game

async function getJson(url) {
  const response = await fetch(url)
  if (response.status === 404) throw new Error('notfound')
  if (!response.ok) throw new Error('failed')
  return response.json()
}

// An "Update from the Armory" button. Asks the server to read this game's characters again from
// Blizzard, then calls reload() to show the new information. The server allows it once every ten minutes.
function refreshButton(reload) {
  const button = el('button', { type: 'button', class: 'button', text: 'Update from the Armory' })
  const note = el('span', { class: 'muted refresh-note', role: 'status' })
  button.addEventListener('click', async () => {
    button.disabled = true
    note.textContent = 'Updating…'
    try {
      const response = await fetch(`../api/${GAME}/refresh`, { method: 'POST' })
      if (response.status === 429) {
        const { wait } = await response.json()
        note.textContent = `Just updated. Try again in ${wait >= 60 ? Math.ceil(wait / 60) + ' min' : wait + ' s'}.`
      } else if (!response.ok) {
        note.textContent = 'The Armory could not be reached right now.'
      } else {
        await reload()
        note.textContent = `Updated ${new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}.`
      }
    } catch {
      note.textContent = 'The update failed. Try again in a little while.'
    } finally {
      button.disabled = false
    }
  })
  return el('div', { class: 'refresh' }, button, note)
}

// "Level 80 Orc Unholy Death Knight"
function describe(c) {
  return [c.level ? `Level ${c.level}` : '', c.race, c.spec, c.class].filter(Boolean).join(' ')
}

function title(text) {
  document.title = `${text} · Lundqwist.se`
}
