// One chapter of a character's story (forever/chapter.html?c=name-surname&n=2)

const page = document.getElementById('chapter')
const params = new URLSearchParams(location.search)
const key = params.get('c') || ''
const number = params.get('n') || ''
const characterUrl = `character.html?c=${encodeURIComponent(key)}`

getJson(`../api/${GAME}/chapter?c=${encodeURIComponent(key)}&n=${encodeURIComponent(number)}`)
  .then(show)
  .catch((e) => {
    page.replaceChildren(
      el('h1', { text: e.message === 'notfound' ? 'Chapter not found' : 'Something went wrong' }),
      el('p', { class: 'lead', text: e.message === 'notfound'
        ? 'There is no such chapter in this story.'
        : 'The chapter could not be loaded. Try again in a little while.' }),
      el('p', {}, el('a', { href: characterUrl, text: '← Back to the character' })))
  })

function show(data) {
  const name = displayName(data.name)
  title(`${data.title} · ${name}`)

  const story = el('div', { class: 'story' })
  story.innerHTML = data.html // made from Markdown on the server, with raw HTML switched off

  page.replaceChildren(
    el('p', {}, el('a', { href: `${characterUrl}#story`, text: `← ${name}: all chapters` })),
    el('p', { class: 'chapter-kicker', text: `${name}'s story · chapter ${data.position} of ${data.count}` }),
    el('h1', { text: data.title }),
    data.subtitle ? el('p', { class: 'chapter-sub', text: data.subtitle }) : null,
    story,
    nav(data))
}

function nav(data) {
  const link = (ch, label) => el('a', { href: `chapter.html?c=${encodeURIComponent(key)}&n=${ch.number}` },
    el('span', { class: 'chapter-nav-label', text: label }), el('span', { text: ch.title }))
  return el('nav', { class: 'chapter-nav', 'aria-label': 'Chapters' },
    data.previous ? link(data.previous, '← Previous') : el('span'),
    el('a', { href: `${characterUrl}#story`, class: 'chapter-nav-all', text: 'All chapters' }),
    data.next ? link(data.next, 'Next →') : el('span'))
}

// "lunamal-sunshadow" -> "Lunamal Sunshadow"
function displayName(n) {
  return (n || '').split(/[-\s]/).filter(Boolean).map((w) => w[0].toUpperCase() + w.slice(1)).join(' ')
}
