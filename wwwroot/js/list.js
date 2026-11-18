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

      // Most chapters first; the same count keeps the order of characters.json (sort is stable)
      const characters = [...data.characters].sort((a, b) => (b.chapters || 0) - (a.chapters || 0))
      for (const c of characters) {
        const card = c.card
        const name = card?.name || c.name
        const info = card
          ? [describe(card), [card.realm || c.realm, card.guild && `<${card.guild}>`].filter(Boolean).join(' ')].filter(Boolean)
          : [c.realm, 'No Armory information'].filter(Boolean)

        const page = `character.html?c=${encodeURIComponent(c.key)}`
        list.append(el('li', { class: 'character-card',
                               style: card?.class && CLASS_COLORS[card.class] ? `--class:${CLASS_COLORS[card.class]}` : null },
          el('a', { class: 'who', href: page },
            card?.avatar ? el('img', { class: 'avatar', src: card.avatar, alt: '', loading: 'lazy' }) : el('div', { class: 'avatar' }),
            el('div', {},
              el('div', { class: 'name', text: name }),
              info.map((line) => el('div', { class: 'info', text: line })))),
          // The story is on the character's page, under the summary
          c.hasStory
            ? el('a', { class: 'button story-link', href: `${page}#story` },
                'Story',
                el('span', { class: 'count', text: c.chapters === 1 ? '1 chapter' : `${c.chapters} chapters` }))
            : el('span', { class: 'muted no-story', text: 'No story yet' })))
      }
    })
}

load().catch(() => (status.textContent = 'The characters could not be loaded. Try again in a little while.'))
