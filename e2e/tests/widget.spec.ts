import { expect, test } from '@playwright/test';

/**
 * Website-chat widget end-to-end (Phase 5, PRD 64):
 *  - a customer site embeds the self-hosted widget (served by the product API),
 *  - the widget opens a session only for an origin the business has allowed,
 *  - the visitor sends a message that lands in the agent's inbox in real-time,
 *  - the agent replies and the visitor sees the reply live — no page refresh.
 */
test('website chat widget: visitor message reaches agent, agent reply reaches visitor live', async ({ browser, playwright, request }) => {
  const api = 'http://localhost:5068';
  const email = `e2e-widget-${Date.now()}-${Math.random().toString(36).slice(2)}@example.test`;
  const password = 'Str0ng!Passw0rd';

  // --- Register the tenant entirely via the API and get the agent token. ---
  const reg = await request.post(`${api}/api/v1/auth/register`, {
    data: {
      email,
      password,
      displayName: 'Widget Owner',
      businessName: `Widget E2E ${Date.now()}`,
      timeZone: 'UTC',
    },
  });
  expect(reg.status()).toBe(200);
  const auth = await reg.json();

  const agentApi = await playwright.request.newContext({ baseURL: api, extraHTTPHeaders: { Authorization: `Bearer ${auth.accessToken}` } });

  // Read the widget settings to learn the embed slug.
  const settingsRes = await agentApi.get('/api/v1/channels/widget');
  expect(settingsRes.status()).toBe(200);
  const settings = await settingsRes.json();
  expect(settings.slug).toBeTruthy();
  const slug = settings.slug;

  // Business allows its own customer-site origin (the demo site is served cross-origin on :5173).
  const allowRes = await agentApi.put('/api/v1/channels/widget/origins', {
    data: { origins: ['http://localhost:5173'] },
  });
  expect(allowRes.status()).toBe(200);

  // --- Agent logs into the inbox. ---
  const agentCtx = await browser.newContext();
  const agentPage = await agentCtx.newPage();
  await agentPage.goto('/login');
  await agentPage.getByLabel('Email').fill(email);
  await agentPage.getByLabel('Password', { exact: true }).fill(password);
  await agentPage.getByRole('button', { name: 'Sign in' }).click();
  await expect(agentPage).toHaveURL(/\/inbox$/);
  await expect(agentPage.getByText('No conversations here')).toBeVisible();
  await agentPage.waitForTimeout(1500); // let the inbox SignalR handshake complete

  // --- Visitor opens the demo customer site (cross-origin) and starts a chat. ---
  const visitorCtx = await browser.newContext();
  const visitorPage = await visitorCtx.newPage();
  await visitorPage.goto(`http://localhost:5173/customer-demo.html?api=${api}&slug=${slug}`);

  // Open the chat widget and send a message.
  await visitorPage.getByRole('button', { name: 'Open chat' }).click();
  await expect(visitorPage.getByRole('dialog', { name: 'Chat with us' })).toBeVisible();

  await visitorPage.getByRole('textbox', { name: 'Message' }).fill('I need help with my order #42.');
  await visitorPage.getByRole('button', { name: 'Send message' }).click();
  // The widget renders the visitor's own message.
  await expect(visitorPage.locator('.ocw__messages').getByText('I need help with my order #42.')).toBeVisible();

  // Give the visitor's SignalR connection time to establish before the agent replies.
  await visitorPage.waitForTimeout(2500);

  // --- Agent sees the conversation + message appear in real-time. ---
  const listPane = agentPage.locator('.list-pane');
  await expect(listPane.getByText('Website visitor')).toBeVisible({ timeout: 5000 });
  await expect(listPane.getByText('I need help with my order #42.')).toBeVisible({ timeout: 5000 });
  await listPane.getByText('Website visitor').click();
  await expect(agentPage).toHaveURL(/\/inbox\/[0-9a-f-]{36}$/);
  await expect(agentPage.locator('.timeline').getByText('I need help with my order #42.')).toBeVisible();

  // --- Agent replies; the visitor must receive it live. ---
  await agentPage.getByLabel('Message').fill('Got it — your order will ship today.');
  await agentPage.getByRole('button', { name: 'Send' }).click();
  await expect(agentPage.locator('.timeline').getByText('Got it — your order will ship today.')).toBeVisible();

  await expect(
    visitorPage.locator('.ocw__messages').getByText('Got it — your order will ship today.')
  ).toBeVisible({ timeout: 5000 });
});
