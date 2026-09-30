import { expect, test } from '@playwright/test'

// Each test gets a fresh browser context, so a fresh localStorage and therefore a fresh cart.

test('browse, add to cart, check out, and watch the order go from Placed to Fulfilled', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Books' })).toBeVisible()

  // The catalogue is seeded into RavenDB on API startup.
  const book = page.getByTestId('book-pride-and-prejudice')
  await expect(book).toContainText('Pride and Prejudice')
  const price = (await page.getByTestId('price-pride-and-prejudice').textContent())!.trim()

  await page.getByTestId('add-pride-and-prejudice').click()
  await expect(page.getByTestId('added-notice')).toContainText('Pride and Prejudice')
  await expect(page.getByTestId('cart-count')).toHaveText('1')

  await page.getByTestId('cart-link').click()
  await expect(page.getByTestId('qty-pride-and-prejudice')).toHaveText('1')
  await expect(page.getByTestId('cart-total')).toHaveText(price)

  await page.getByTestId('email').fill('reader@example.com')
  await page.getByTestId('checkout').click()

  // Checkout returns straight away with the order Placed; the worker fulfils it after its simulated 2 s of work.
  await expect(page.getByRole('heading', { name: 'Order confirmed' })).toBeVisible()
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]{36}$/)
  await expect(page.getByTestId('order-total')).toHaveText(price)
  // The purchase is complete before fulfilment has run: the page first shows Placed, then the status changes.
  await expect(page.getByTestId('order-status')).toHaveText('Placed')
  await expect(page.getByTestId('order-status')).toHaveText('Fulfilled', { timeout: 30_000 })

  // The cart was emptied in the same transaction that saved the order.
  await expect(page.getByTestId('cart-count')).toHaveText('0')
})

test('adding a book twice doubles the line and the total uses the catalogue price', async ({ page }) => {
  await page.goto('/')
  const price = Number((await page.getByTestId('price-dracula').textContent())!.replace('$', ''))

  await page.getByTestId('add-dracula').click()
  await expect(page.getByTestId('cart-count')).toHaveText('1')
  await page.getByTestId('add-dracula').click()
  await expect(page.getByTestId('cart-count')).toHaveText('2')

  await page.getByTestId('cart-link').click()
  await expect(page.getByTestId('qty-dracula')).toHaveText('2')
  await expect(page.getByTestId('cart-total')).toHaveText(`$${(price * 2).toFixed(2)}`)
})

test('checking out an empty cart shows the error from the API and stays on the cart', async ({ page }) => {
  await page.goto('/cart')
  await expect(page.getByTestId('cart-empty')).toBeVisible()

  await page.getByTestId('email').fill('reader@example.com')
  await page.getByTestId('checkout').click()

  await expect(page.getByTestId('checkout-errors')).toContainText('The cart is empty.')
  await expect(page).toHaveURL(/\/cart$/)
})
