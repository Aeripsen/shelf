import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, ApiError, money, type Order } from '../api'

// Checkout returns as soon as the order is saved and OrderPlaced is published. Fulfilment happens later in the
// worker, so this page polls the order until its status turns from Placed to Fulfilled.
export function OrderPage() {
  const { id = '' } = useParams()
  const [order, setOrder] = useState<Order | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let stopped = false
    let timer: number | undefined

    async function poll() {
      try {
        const latest = await api.order(id)
        if (stopped) return
        setOrder(latest)
        setError(null)
        if (latest.status !== 'Fulfilled') timer = window.setTimeout(poll, 1000)
      } catch (e) {
        if (stopped) return
        if (e instanceof ApiError && e.status === 404) {
          setError('Order not found.')
          return
        }
        // A network blip or a 5xx: keep showing the last known order and try again shortly.
        setError('Could not refresh the order, retrying...')
        timer = window.setTimeout(poll, 2000)
      }
    }

    void poll()
    return () => {
      stopped = true
      window.clearTimeout(timer)
    }
  }, [id])

  if (!order) return error ? <p className="error">{error}</p> : <p className="muted">Loading your order...</p>

  return (
    <section>
      <h1>Order confirmed</h1>
      <p>
        Order <code data-testid="order-id">{order.orderId}</code> for {order.email}.
      </p>
      <p>
        Status:{' '}
        <strong data-testid="order-status" className={`status status-${order.status.toLowerCase()}`}>
          {order.status}
        </strong>
      </p>
      {error && <p className="error">{error}</p>}
      {order.status === 'Placed' && (
        <p className="muted">Your purchase is complete. Fulfilment is running in the background.</p>
      )}
      <table className="lines">
        <tbody>
          {order.lines.map((line) => (
            <tr key={line.bookId}>
              <td>{line.title}</td>
              <td>x {line.quantity}</td>
              <td>{money(line.unitPrice * line.quantity)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td colSpan={2}>Total</td>
            <td data-testid="order-total">{money(order.total)}</td>
          </tr>
        </tfoot>
      </table>
    </section>
  )
}
