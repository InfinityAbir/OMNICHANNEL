import { expect, test } from '@playwright/test';

function uniqueEmail(): string {
  return `e2e-rt-${Date.now()}-${Math.random().toString(36).slice(2)}@example.test`;
}

/**
 * Two agents in the same tenant each open the inbox. Agent A creates a conversation and
 * sends a message; Agent B sees it appear in their list without a page refresh, proving
 * the SignalR realtime pipeline delivers new-message and conversation-update events
 * end-to-end through the whole stack.
 */
test('second agent sees new conversation appear in real-time via SignalR', async ({ browser }) => {
  const email = uniqueEmail();
  const password = 'Str0ng!Passw0rd';

  // --- Agent A registers the tenant ---
  const contextA = await browser.newContext();
  const pageA = await contextA.newPage();

  await pageA.goto('/register');
  await pageA.getByLabel('Business name').fill('RT E2E Business');
  await pageA.getByLabel('Your name').fill('Agent A');
  await pageA.getByLabel('Email').fill(email);
  await pageA.getByLabel('Password', { exact: true }).fill(password);
  await pageA.getByRole('button', { name: 'Create account' }).click();
  await expect(pageA).toHaveURL(/\/inbox$/);
  await expect(pageA.getByText('No conversations here')).toBeVisible();

  // --- Agent B logs into the same tenant using the same credentials ---
  const contextB = await browser.newContext();
  const pageB = await contextB.newPage();

  await pageB.goto('/login');
  await pageB.getByLabel('Email').fill(email);
  await pageB.getByLabel('Password', { exact: true }).fill(password);
  await pageB.getByRole('button', { name: 'Sign in' }).click();
  await expect(pageB).toHaveURL(/\/inbox$/);
  await expect(pageB.getByText('No conversations here')).toBeVisible();

  // Wait for the SignalR connection to be established on both pages.
  // The hub connects on login/register; give it a moment to handshake.
  await pageA.waitForTimeout(1500);
  await pageB.waitForTimeout(1500);

  // --- Agent A creates a conversation with an initial message ---
  await pageA.getByRole('button', { name: '+ New conversation' }).click();
  await pageA.getByLabel('Customer name').fill('Realtime Customer');
  await pageA.getByLabel('First message (optional)').fill('Is anyone there?');
  await pageA.getByRole('button', { name: 'Create' }).click();
  await expect(pageA).toHaveURL(/\/inbox\/[0-9a-f-]{36}$/);
  await expect(pageA.getByRole('heading', { name: 'Realtime Customer' })).toBeVisible();

  // --- Agent B should see the conversation appear in the list without reloading ---
  const listPaneB = pageB.locator('.list-pane');
  await expect(listPaneB.getByText('Realtime Customer')).toBeVisible({ timeout: 5000 });
  await expect(listPaneB.getByText('Is anyone there?')).toBeVisible({ timeout: 5000 });
});

/**
 * Two agents open the same conversation. Agent A sends a reply; Agent B sees the message
 * appear in the timeline without a page refresh.
 */
test('second agent sees new message appear in real-time in the same conversation', async ({ browser }) => {
  const email = uniqueEmail();
  const password = 'Str0ng!Passw0rd';

  // --- Register tenant via Agent A ---
  const contextA = await browser.newContext();
  const pageA = await contextA.newPage();

  await pageA.goto('/register');
  await pageA.getByLabel('Business name').fill('RT E2E Business 2');
  await pageA.getByLabel('Your name').fill('Agent A');
  await pageA.getByLabel('Email').fill(email);
  await pageA.getByLabel('Password', { exact: true }).fill(password);
  await pageA.getByRole('button', { name: 'Create account' }).click();
  await expect(pageA).toHaveURL(/\/inbox$/);

  // --- Agent A creates a conversation ---
  await pageA.getByRole('button', { name: '+ New conversation' }).click();
  await pageA.getByLabel('Customer name').fill('Chat Customer');
  await pageA.getByLabel('First message (optional)').fill('Hello');
  await pageA.getByRole('button', { name: 'Create' }).click();
  await expect(pageA).toHaveURL(/\/inbox\/[0-9a-f-]{36}$/);

  // --- Agent B logs in and opens the same conversation ---
  const contextB = await browser.newContext();
  const pageB = await contextB.newPage();

  await pageB.goto('/login');
  await pageB.getByLabel('Email').fill(email);
  await pageB.getByLabel('Password', { exact: true }).fill(password);
  await pageB.getByRole('button', { name: 'Sign in' }).click();
  await expect(pageB).toHaveURL(/\/inbox$/);

  // Wait for SignalR connections on both pages.
  await pageA.waitForTimeout(1500);
  await pageB.waitForTimeout(1500);

  // Agent B navigates to the same conversation by clicking it in the list.
  const listPaneB = pageB.locator('.list-pane');
  await expect(listPaneB.getByText('Chat Customer')).toBeVisible({ timeout: 5000 });
  await listPaneB.getByText('Chat Customer').click();
  await expect(pageB).toHaveURL(/\/inbox\/[0-9a-f-]{36}$/);

  const timelineB = pageB.locator('.timeline');
  await expect(timelineB.getByText('Hello')).toBeVisible();

  // --- Agent A sends a reply ---
  const timelineA = pageA.locator('.timeline');
  await pageA.getByLabel('Message').fill('Hi there!');
  await pageA.getByRole('button', { name: 'Send' }).click();
  await expect(timelineA.getByText('Hi there!')).toBeVisible();

  // --- Agent B should see it in real-time without refreshing ---
  await expect(timelineB.getByText('Hi there!')).toBeVisible({ timeout: 5000 });
});
