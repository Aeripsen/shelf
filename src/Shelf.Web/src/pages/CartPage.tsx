import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError, money, type Cart } from '../api'

type Props = {
  cart: Cart | null
  onCartChanged: (cart: Cart) => void
  refreshCart: () => Promise<void>
}

export function CartPage({ cart, onCartChanged, refreshCart }: Props) {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    void refreshCart()
  }, [refreshCart])

  async function remove(bookId: string) {
    onCartChanged(await api.removeFromCart(bookId))
  }

  async function checkout(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setErrors([])
    try {
      const result = await api.checkout(email)
      await refreshCart()
      navigate(`/orders/${result.orderId}`)
    } catch (e) {
      setErrors(e instanceof ApiError ? e.problems : ['Checkout failed. Please try again.'])
    } finally {
      setSubmitting(false)
    }
  }

  if (!cart) return <p className="muted">Loading your cart...</p>

  return (
    <section>
      <h1>Your cart</h1>
      {cart.items.length === 0 ? (
        <p data-testid="cart-empty">
          Your cart is empty. <Link to="/">Browse the books</Link>.
        </p>
      ) : (
        <table className="lines" data-testid="cart-lines">
          <thead>
            <tr>
              <th>Book</th>
              <th>Qty</th>
              <th>Price</th>
              <th>Subtotal</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {cart.items.map((line) => (
              <tr key={line.bookId} data-testid={`line-${line.bookId}`}>
                <td>{line.available ? line.title : `${line.bookId} (no longer available)`}</td>
                <td data-testid={`qty-${line.bookId}`}>{line.quantity}</td>
                <td>{money(line.unitPrice)}</td>
                <td>{money(line.lineTotal)}</td>
                <td>
                  <button className="link" data-testid={`remove-${line.bookId}`} onClick={() => void remove(line.bookId)}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={3}>Total</td>
              <td data-testid="cart-total">{money(cart.total)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      )}

      <form className="checkout" onSubmit={(e) => void checkout(e)}>
        <label htmlFor="email">Email for the receipt</label>
        <input
          id="email"
          data-testid="email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="you@example.com"
          required
        />
        <button type="submit" data-testid="checkout" disabled={submitting}>
          {submitting ? 'Placing order...' : 'Place order'}
        </button>
      </form>

      {errors.length > 0 && (
        <ul className="error" data-testid="checkout-errors">
          {errors.map((error) => (
            <li key={error}>{error}</li>
          ))}
        </ul>
      )}
    </section>
  )
}
