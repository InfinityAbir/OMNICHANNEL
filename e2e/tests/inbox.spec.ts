import { expect, test } from '@playwright/test';

function uniqueEmail(): string {
  return `e2e-${Date.now()}-${Math.random().toString(36).slice(2)}@example.test`;
}

test('register, create a conversation, and send a message end to end', async ({ page }) => {
  const email = uniqueEmail();

  // --- Register ---
  await page.goto('/register');
  await page.getByLabel('Business name').fill('E2E Test Business');
  await page.getByLabel('Your name').fill('E2E Owner');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Str0ng!Passw0rd');
  await page.getByRole('button', { name: 'Create account' }).click();

  // --- Lands on the inbox, authenticated ---
  await expect(page).toHaveURL(/\/inbox$/);
  await expect(page.getByText('E2E Test Business')).toBeVisible();
  await expect(page.getByText('No conversations here')).toBeVisible();

  // --- Create a conversation ---
  await page.getByRole('button', { name: '+ New conversation' }).click();
  await page.getByLabel('Customer name').fill('E2E Customer');
  await page.getByLabel('First message (optional)').fill('Hello, is anyone there?');
  await page.getByRole('button', { name: 'Create' }).click();

  // --- Lands on the new conversation's detail view ---
  await expect(page).toHaveURL(/\/inbox\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: 'E2E Customer' })).toBeVisible();
  const timeline = page.locator('.timeline');
  await expect(timeline.getByText('Hello, is anyone there?')).toBeVisible();

  // --- Reply ---
  await page.getByLabel('Message').fill('Yes, how can I help?');
  await page.getByRole('button', { name: 'Send' }).click();
  await expect(timeline.getByText('Yes, how can I help?')).toBeVisible();

  // --- Shows up in the conversation list with the latest preview (list and detail panes
  // sit side by side at this viewport width — the list is already visible, no navigation
  // needed; "Back to conversation list" only appears on narrow/mobile layouts). ---
  const listPane = page.locator('.list-pane');
  await expect(listPane.getByText('E2E Customer')).toBeVisible();
  await expect(listPane.getByText('Yes, how can I help?')).toBeVisible();

  // --- Sign out returns to login ---
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test('rejects an unauthenticated visit to the inbox', async ({ page }) => {
  await page.goto('/inbox');
  await expect(page).toHaveURL(/\/login$/);
});
