import { useCallback, useEffect, useState } from 'react'
import { Link, Route, Routes } from 'react-router-dom'
import { api, type Cart } from './api'
import { BookList } from './pages/BookList'
import { CartPage } from './pages/CartPage'
import { OrderPage } from './pages/OrderPage'

export function App() {
  const [cart, setCart] = useState<Cart | null>(null)

  const refreshCart = useCallback(async () => {
    try {
      setCart(await api.cart())
    } catch {
      setCart(null)
    }
  }, [])

  // Load the cart once on start. The state is set in the promise callback, after the effect has returned.
  useEffect(() => {
    let stopped = false
    api
      .cart()
      .then((loaded) => !stopped && setCart(loaded))
      .catch(() => !stopped && setCart(null))
    return () => {
      stopped = true
    }
  }, [])

  const count = cart?.items.reduce((sum, line) => sum + line.quantity, 0) ?? 0

  return (
    <div className="page">
      <header className="top">
        <Link to="/" className="brand">
          Shelf
        </Link>
        <nav>
          <Link to="/cart" data-testid="cart-link">
            Cart <span className="badge" data-testid="cart-count">{count}</span>
          </Link>
        </nav>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<BookList onCartChanged={setCart} />} />
          <Route path="/cart" element={<CartPage cart={cart} onCartChanged={setCart} refreshCart={refreshCart} />} />
          <Route path="/orders/:id" element={<OrderPage />} />
        </Routes>
      </main>

      <footer className="bottom">Demo store. Prices are sample values and nothing is charged.</footer>
    </div>
  )
}
