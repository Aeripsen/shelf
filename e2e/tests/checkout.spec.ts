import { expect, test, type Page } from '@playwright/test'

// Each test gets a fresh browser context, so a fresh localStorage and therefore a fresh cart.

/** Runs one named step, then saves a full-page screenshot of where it ended as demo/<prefix>-<name>.png. */
async function step(page: Page, prefix: string, name: string, body: () => Promise<void>) {
  await test.step(name, body)
  const file = `demo/${prefix}-${name.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.png`
  await page.screenshot({ path: file, fullPage: true })
  await test.info().attach(name, { path: file, contentType: 'image/png' })
}

test('browse, add to cart, check out, and watch the order go from Placed to Fulfilled', async ({ page }) => {
  let price = ''

  await step(page, 'checkout-1', 'catalogue from RavenDB', async () => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Books' })).toBeVisible()
    // The catalogue is seeded into RavenDB on API startup.
    await expect(page.getByTestId('book-pride-and-prejudice')).toContainText('Pride and Prejudice')
    price = (await page.getByTestId('price-pride-and-prejudice').textContent())!.trim()
  })

  await step(page, 'checkout-2', 'added to cart', async () => {
    await page.getByTestId('add-pride-and-prejudice').click()
    await expect(page.getByTestId('added-notice')).toContainText('Pride and Prejudice')
    await expect(page.getByTestId('cart-count')).toHaveText('1')
  })

  await step(page, 'checkout-3', 'cart priced from the catalogue', async () => {
    await page.getByTestId('cart-link').click()
    await expect(page.getByTestId('qty-pride-and-prejudice')).toHaveText('1')
    await expect(page.getByTestId('cart-total')).toHaveText(price)
    await page.getByTestId('email').fill('reader@example.com')
  })

  await step(page, 'checkout-4', 'order placed', async () => {
    await page.getByTestId('checkout').click()
    // Checkout returns straight away with the order Placed; the worker fulfils it after its simulated 2 s of work.
    await expect(page.getByRole('heading', { name: 'Order confirmed' })).toBeVisible()
    await expect(page).toHaveURL(/\/orders\/[0-9a-f-]{36}$/)
    await expect(page.getByTestId('order-total')).toHaveText(price)
    // The purchase is complete before fulfilment has run: the page first shows Placed, then the status changes.
    await expect(page.getByTestId('order-status')).toHaveText('Placed')
  })

  await step(page, 'checkout-5', 'order fulfilled by the worker', async () => {
    await expect(page.getByTestId('order-status')).toHaveText('Fulfilled', { timeout: 30_000 })
    // After a successful checkout the storefront's cart count is back to zero.
    await expect(page.getByTestId('cart-count')).toHaveText('0')
  })
})

test('adding a book twice doubles the line, and Remove takes a line out of the cart', async ({ page }) => {
  await page.goto('/')
  const dracula = Number((await page.getByTestId('price-dracula').textContent())!.replace('$', ''))
  const emma = Number((await page.getByTestId('price-emma').textContent())!.replace('$', ''))

  await step(page, 'cart-1', 'two books in the cart', async () => {
    await page.getByTestId('add-dracula').click()
    await expect(page.getByTestId('cart-count')).toHaveText('1')
    await page.getByTestId('add-dracula').click()
    await expect(page.getByTestId('cart-count')).toHaveText('2')
    await page.getByTestId('add-emma').click()
    await expect(page.getByTestId('cart-count')).toHaveText('3')

    await page.getByTestId('cart-link').click()
    await expect(page.getByTestId('qty-dracula')).toHaveText('2')
    await expect(page.getByTestId('cart-total')).toHaveText(`$${(dracula * 2 + emma).toFixed(2)}`)
  })

  await step(page, 'cart-2', 'line removed', async () => {
    await page.getByTestId('remove-emma').click()
    await expect(page.getByTestId('line-emma')).toHaveCount(0)
    await expect(page.getByTestId('cart-total')).toHaveText(`$${(dracula * 2).toFixed(2)}`)
    await expect(page.getByTestId('cart-count')).toHaveText('2')
  })

  // The removal is stored by the API, not only hidden in the page.
  await page.reload()
  await expect(page.getByTestId('qty-dracula')).toHaveText('2')
  await expect(page.getByTestId('line-emma')).toHaveCount(0)
})

test('search finds books by author through the RavenDB index', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('book-dracula')).toBeVisible()

  await step(page, 'search-1', 'search for bronte', async () => {
    await page.getByTestId('search').fill('bronte')
    await page.getByTestId('search-submit').click()
    await expect(page.getByTestId('book-jane-eyre')).toBeVisible()
    await expect(page.getByTestId('book-wuthering-heights')).toBeVisible()
    await expect(page.getByTestId('book-dracula')).toHaveCount(0)
  })

  await page.getByTestId('search').fill('ulysses')
  await page.getByTestId('search-submit').click()
  await expect(page.getByTestId('no-results')).toBeVisible()
})

test('checking out an empty cart shows the error from the API and stays on the cart', async ({ page }) => {
  await page.goto('/cart')
  await expect(page.getByTestId('cart-empty')).toBeVisible()

  await page.getByTestId('email').fill('reader@example.com')
  await page.getByTestId('checkout').click()

  await expect(page.getByTestId('checkout-errors')).toContainText('The cart is empty.')
  await expect(page).toHaveURL(/\/cart$/)
})
