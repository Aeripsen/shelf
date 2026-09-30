import { useEffect, useState } from 'react'
import { api, ApiError, money, type Book, type Cart } from '../api'

type Props = {
  onCartChanged: (cart: Cart) => void
}

export function BookList({ onCartChanged }: Props) {
  const [books, setBooks] = useState<Book[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [added, setAdded] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    api
      .books()
      .then(setBooks)
      .catch((e: unknown) => setError(e instanceof ApiError ? e.message : 'Could not load the catalogue.'))
  }, [])

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
