import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, money, type Book, type Cart } from '../api'

type Props = {
  onCartChanged: (cart: Cart) => void
}

export function BookList({ onCartChanged }: Props) {
  const [books, setBooks] = useState<Book[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [added, setAdded] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  // Each submission gets a new object, so searching the same text again (after a failure) fetches again.
  const [submitted, setSubmitted] = useState({ text: '' })
  const searched = submitted.text

  useEffect(() => {
    // Only the latest search may update the page: a slower, older response is ignored when it arrives.
    let stale = false
    api
      .books(submitted.text)
      .then((found) => !stale && setBooks(found))
      .catch((e: unknown) => !stale && setError(e instanceof ApiError ? e.message : 'Could not load the catalogue.'))
    return () => {
      stale = true
    }
  }, [submitted])

  function search(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSubmitted({ text: query.trim() })
  }

  async function add(book: Book) {
    setBusy(book.slug)
    setError(null)
    try {
      onCartChanged(await api.addToCart(book.slug))
      setAdded(book.title)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not add to cart.')
    } finally {
      setBusy(null)
    }
  }

  if (error && !books) return <p className="error">{error}</p>
  if (!books) return <p className="muted">Loading the catalogue...</p>

  return (
    <section>
      <h1>Books</h1>
      <form className="search" role="search" onSubmit={search}>
        <input
          aria-label="Search by title or author"
          data-testid="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search by title or author"
        />
        <button type="submit" data-testid="search-submit">
          Search
        </button>
      </form>
      {searched && books.length === 0 && (
        <p className="muted" data-testid="no-results">
          No books match "{searched}".
        </p>
      )}
      {added && (
        <p className="notice" data-testid="added-notice">
          Added {added} to your cart.
        </p>
      )}
      {error && <p className="error">{error}</p>}
      <ul className="grid" data-testid="book-list">
        {books.map((book) => (
          <li key={book.slug} className="card" data-testid={`book-${book.slug}`}>
            <h2>{book.title}</h2>
            <p className="muted">
              {book.author}, {book.year}
            </p>
            <p className="price" data-testid={`price-${book.slug}`}>
              {money(book.price)}
            </p>
            <button data-testid={`add-${book.slug}`} disabled={busy === book.slug} onClick={() => void add(book)}>
              Add to cart
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
