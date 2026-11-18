// The list page of one game (retail/index.html, forever/index.html)

const list = document.getElementById('characters')
const status = document.getElementById('status')
let refreshShown = false

function load() {
  return getJson(`../api/${GAME}/characters`)
    .then((data) => {
      status.textContent = ''
      list.replaceChildren()
      if (data.characters.length === 0) {
        status.textContent = 'No characters here yet.'
        return
      }
      if (!data.armory) status.textContent = 'Armory information is not available right now.'
      else if (!refreshShown) {
        list.before(refreshButton(load))
        refreshShown = true
      }

      for (const c of data.characters) {
        const card = c.card
        const name = card?.name || c.name
        const info = card
          ? [describe(card), [card.realm || c.realm, card.guild && `<${card.guild}>`].filter(Boolean).join(' ')].filter(Boolean)
          : [c.realm, 'No Armory information'].filter(Boolean)

        list.append(el('li', {},
          el('a', { class: 'character-card', href: `character.html?c=${encodeURIComponent(c.key)}`,
                    style: card?.class && CLASS_COLORS[card.class] ? `--class:${CLASS_COLORS[card.class]}` : null },
            card?.avatar ? el('img', { class: 'avatar', src: card.avatar, alt: '', loading: 'lazy' }) : el('div', { class: 'avatar' }),
            el('div', {},
              el('div', { class: 'name', text: name }),
              info.map((line) => el('div', { class: 'info', text: line })),
              c.hasStory ? el('div', { class: 'info', text: '📖 Has a story' }) : null))))
      }
    })
}

load().catch(() => (status.textContent = 'The characters could not be loaded. Try again in a little while.'))
