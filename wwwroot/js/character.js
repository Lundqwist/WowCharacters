// One character's page (retail/character.html?c=draenor/moonie, forever/character.html?c=name-surname)

const page = document.getElementById('character')
const key = new URLSearchParams(location.search).get('c') || ''

// Item links go to Wowhead; its tooltip script (power.js) then shows the item on hover
const WOWHEAD = GAME === 'forever' ? 'https://www.wowhead.com/forever/' : 'https://www.wowhead.com/'

getJson(`../api/${GAME}/character?c=${encodeURIComponent(key)}`)
  .then(show)
  .catch((e) => {
    page.replaceChildren(
      el('h1', { text: e.message === 'notfound' ? 'Character not found' : 'Something went wrong' }),
      el('p', { class: 'lead', text: e.message === 'notfound'
        ? 'There is no such character on this page.'
        : 'The character could not be loaded. Try again in a little while.' }),
      el('p', {}, el('a', { href: './', text: '← All characters' })))
  })

function show(data) {
  const s = data.sheet
  const name = s?.name || data.name
  title(`${name} (${GAME === 'forever' ? 'WoW: Forever' : 'WoW'})`)

  const parts = [
    el('p', {}, el('a', { href: './', text: '← All characters' })),
    el('h1', { text: name, style: s?.class && CLASS_COLORS[s.class] ? `color:${CLASS_COLORS[s.class]}` : null }),
  ]

  if (!s) {
    parts.push(el('p', { class: 'notice', text: 'Armory information is not available for this character right now.' }))
  } else {
    parts.push(
      s.title && s.title !== name ? el('p', { class: 'char-title', text: s.title }) : null,
      el('div', { class: 'sheet-head' },
        el('div', { class: 'render' }, s.render || s.avatar ? el('img', { src: s.render || s.avatar, alt: `${name}` }) : null),
        el('div', {},
          el('dl', { class: 'facts panel' }, facts(s, data).map(([k, v]) => [el('dt', { text: k }), el('dd', { text: v })])),
          el('div', { class: 'buttons' },
            el('a', { class: 'button', href: data.armoryUrl, target: '_blank', rel: 'noopener', text: 'View on the Armory' })))))
  }

  // The story comes first after the summary: it is what the page is really for
  parts.push(el('h2', { text: 'Story' }))
  if (data.storyHtml) {
    const story = el('div', { class: 'story' })
    story.innerHTML = data.storyHtml // made from Markdown on the server, with raw HTML switched off
    parts.push(story)
  } else {
    parts.push(el('p', { class: 'muted', text: 'The story of this character is still to be written.' }))
  }

  if (s?.stats?.length) {
    parts.push(el('h2', { text: 'Stats' }),
      el('ul', { class: 'stats' }, s.stats.map((x) => el('li', {}, el('span', { text: x.label }), el('span', { text: x.value })))))
  }

  if (s?.gear?.length) {
    parts.push(el('h2', { text: 'Gear' }),
      el('table', { class: 'gear' },
        el('thead', {}, el('tr', {}, el('th', { text: 'Slot' }), el('th', { text: 'Item' }), el('th', { class: 'num', text: 'Item level' }))),
        el('tbody', {}, s.gear.map((g) => el('tr', {},
          el('td', { class: 'muted', text: g.slot }),
          el('td', {}, el('a', { class: `q-${g.quality}`, href: `${WOWHEAD}item=${g.id}`, target: '_blank', rel: 'noopener', text: g.name })),
          el('td', { class: 'num', text: g.level || '' }))))))
  }

  if (s?.professions?.length) {
    parts.push(el('h2', { text: 'Professions' }),
      el('ul', { class: 'professions' }, s.professions.map((p) => {
        const tiers = p.tiers.filter((t) => t.skill > 1 || p.tiers.length === 1)
        return el('li', {},
          el('div', { text: p.name }),
          el('div', { class: 'tiers', text: tiers.map((t) => `${t.name ? t.name + ': ' : ''}${t.skill}/${t.max}`).join(' · ') }))
      })))
  }

  page.replaceChildren(...parts.filter(Boolean))
}

// The realm: the Armory's name for it, or for Forever (which has no realms in the API) the label in characters.json
function facts(s, data) {
  const rows = [
    ['Level', s.level],
    ['Race', s.race],
    ['Class', [s.spec, s.class].filter(Boolean).join(' ')],
    ['Faction', s.faction],
    ['Realm', s.realm || data.realm],
    ['Guild', s.guild],
    ['Item level', s.itemLevel],
    ['Achievement points', s.achievementPoints ? s.achievementPoints.toLocaleString('en-GB') : ''],
    ['Last seen', s.lastLogin ? new Date(s.lastLogin).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' }) : ''],
  ]
  return rows.filter(([, v]) => v)
}
