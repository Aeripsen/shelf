// Thin fetch wrapper over the Shelf API. Every cart and checkout call carries the X-Cart-Id header,
// a random id this browser keeps in localStorage.

export type Book = {
  slug: string
  title: string
  author: string
  year: number
  price: number
}

export type CartLine = {
  bookId: string
  title: string
  unitPrice: number
  quantity: number
  lineTotal: number
  available: boolean
}

export type Cart = {
  cartId: string
  items: CartLine[]
  total: number
}

export type OrderStatus = 'Placed' | 'Fulfilled'

export type OrderLine = {
  bookId: string
  title: string
  unitPrice: number
  quantity: number
}

export type Order = {
  orderId: string
  email: string
  status: OrderStatus
  total: number
  placedAtUtc: string
  fulfilledAtUtc: string | null
  lines: OrderLine[]
}

export type CheckoutResult = {
  orderId: string
  status: OrderStatus
  total: number
}

type Problem = {
  title?: string
  detail?: string
  errors?: string[]
}

export class ApiError extends Error {
  readonly status: number
  readonly problems: string[]

  constructor(status: number, problems: string[]) {
    super(problems.join(' '))
    this.status = status
    this.problems = problems
  }
}

const CART_KEY = 'shelf.cartId'

export function cartId(): string {
  let id = localStorage.getItem(CART_KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(CART_KEY, id)
  }
  return id
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Cart-Id': cartId(),
      ...(init.headers ?? {}),
    },
  })

  if (!response.ok) {
    let problems = [`Request failed with status ${response.status}.`]
    try {
      const problem = (await response.json()) as Problem
      if (problem.errors && problem.errors.length > 0) problems = problem.errors
      else if (problem.detail) problems = [problem.detail]
      else if (problem.title) problems = [problem.title]
    } catch {
      // body was not JSON; keep the generic message
    }
    throw new ApiError(response.status, problems)
  }

  return (await response.json()) as T
}

export const api = {
  books: () => request<Book[]>('/api/books'),
  cart: () => request<Cart>('/api/cart'),
  addToCart: (bookId: string, quantity = 1) =>
    request<Cart>('/api/cart/items', { method: 'POST', body: JSON.stringify({ bookId, quantity }) }),
  removeFromCart: (bookId: string) =>
    request<Cart>(`/api/cart/items/${encodeURIComponent(bookId)}`, { method: 'DELETE' }),
  checkout: (email: string) =>
    request<CheckoutResult>('/api/checkout', { method: 'POST', body: JSON.stringify({ email }) }),
  order: (id: string) => request<Order>(`/api/orders/${encodeURIComponent(id)}`),
}

export const money = (value: number) => `$${value.toFixed(2)}`
