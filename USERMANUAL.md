# Omnichannel — User Manual

**A complete guide to using Omnichannel, written so anyone can follow it — no computer
experience needed.** If you can send a text message and use email, you can use this app.

---

## Table of contents

1. [What is Omnichannel?](#1-what-is-omnichannel)
2. [Creating your account](#2-creating-your-account)
3. [Signing in and out](#3-signing-in-and-out)
4. [The Inbox — your home screen](#4-the-inbox--your-home-screen)
5. [Connecting your messaging channels](#5-connecting-your-messaging-channels)
6. [Teaching the AI about your business (Knowledge Base)](#6-teaching-the-ai-about-your-business-knowledge-base)
7. [Letting AI help you reply](#7-letting-ai-help-you-reply)
8. [Automation Rules](#8-automation-rules)
9. [Business Hours](#9-business-hours)
10. [Saved Replies](#10-saved-replies)
11. [Email settings (SMTP)](#11-email-settings-smtp)
12. [Analytics](#12-analytics)
13. [Notifications (the bell icon)](#13-notifications-the-bell-icon)
14. [Team roles — who can do what](#14-team-roles--who-can-do-what)
15. [Your account and your business account](#15-your-account-and-your-business-account)
16. [Staying safe](#16-staying-safe)
17. [Troubleshooting / "It's not working"](#17-troubleshooting--its-not-working)
18. [Glossary — plain-English meaning of every term](#18-glossary--plain-english-meaning-of-every-term)

---

## 1. What is Omnichannel?

Imagine your customers message you on WhatsApp, Instagram, Facebook Messenger, and through the
chat box on your website — all at once. Normally you'd have to open four different apps to keep
up. **Omnichannel puts every one of those conversations into one single screen**, so you never
miss a message no matter which app your customer used.

On top of that, Omnichannel has a built-in AI assistant that can:
- **Suggest** a reply for you to read, edit, and send yourself, or
- **Automatically reply** to simple questions on its own, within limits you control, and hand off
  to a real person the moment something needs a human touch (a refund request, a complaint,
  anything it isn't sure about).

Nothing the AI writes is ever sent to a customer without either your approval (Suggest mode) or
your explicit permission to let it send automatically (Auto-Reply mode, which you turn on
yourself and can turn off any time).

---

## 2. Creating your account

You don't need anyone to set this up for you — you create your own business account in under a
minute.

1. Open the app in your web browser (your manager or IT person will give you the web address —
   it looks like `https://something.onrender.com` or your own company's address).
2. Click **Create one** under the sign-in box.
3. Fill in four things:
   - **Business name** — your company or shop's name.
   - **Your name** — how your name should appear to your team.
   - **Email** — the email address you'll use to sign in. Use a real one you check.
   - **Password** — pick a strong password (at least 10 characters, mixing upper/lowercase
     letters, a number, and a symbol).
4. Click **Create account**.

That's it — you're in. Creating an account automatically makes **you the Owner** of your new
business account, which means you have full control (see [section 14](#14-team-roles--who-can-do-what)
for what that means).

You'll also see a "Manual" channel and a "Website Chat" channel already set up for you, so you can
start a conversation and try things out immediately, even before connecting WhatsApp or any other
app.

> **A note about the confirmation email:** if your business hasn't set up its own email sending
> yet (see [section 11](#11-email-settings-smtp)), you might not receive a welcome email — that's
> fine, it doesn't stop you from using the app. Nothing is blocked by an unconfirmed email unless
> your administrator has specifically turned that requirement on.

---

## 3. Signing in and out

- **Signing in**: go to the app's web address, enter your email and password, click **Sign in**.
- **Forgot your password?** Click the password-reset link on the sign-in screen (if shown), enter
  your email, and check your inbox for a reset link. If your business's email sending isn't set up
  yet, ask your Owner/Admin to help — a link cannot be emailed to you until that's configured.
- **Signing out**: click **Sign out** in the top-right corner of the screen. Do this on any shared
  or public computer when you're done.
- Your sign-in stays active for a while automatically so you don't have to log in every few
  minutes — but it does eventually expire on its own for security, at which point you'll just be
  asked to sign in again.

---

## 4. The Inbox — your home screen

When you sign in, you land on the **Inbox**. This is where all your customer conversations live,
no matter which app the customer messaged you from.

### Reading and replying to a conversation

1. Click any conversation in the left-hand list to open it.
2. You'll see the full message history — customer messages on one side, your team's replies on
   the other.
3. Type your reply in the box at the bottom and click **Send** (or press Enter).

### Starting a new conversation yourself

Click **+ New conversation** at the top of the Inbox, type the customer's name and (optionally) a
first message, then click **Create**. Useful when a customer called you or walked in, and you want
to keep a record of it alongside your other conversations.

### Organizing conversations

Each conversation has a few controls at the top:

- **Status** — where things stand: `Open`, `Pending`, `Escalated`, `Resolved`, or `Closed`. Change
  it from the dropdown as work on the conversation progresses.
- **Priority** — how urgent it is. Helps your team decide what to handle first.
- **Assign to me / Unassign** — claim a conversation as yours, or release it back for someone else
  to pick up.
- **Tags** — add short labels (like "refund" or "VIP") to a conversation so you can find similar
  ones later. Click **+ Add tag** to pick an existing one, or type a new tag name and press Enter.
- **AI mode** — controls what the AI assistant does for *this specific conversation*: nothing,
  suggest a reply for you to review, or reply automatically. Explained fully in
  [section 7](#7-letting-ai-help-you-reply).

### Internal notes

Click the **Internal notes** tab inside a conversation to leave a note for your teammates —
**customers never see these**. Good for "called them back, no answer" or "check with billing
before replying."

### Saved replies

If your business has set up canned responses (see [section 10](#10-saved-replies)), you'll see an
**Insert…** dropdown above the reply box — pick one to drop a pre-written message into your reply,
which you can still edit before sending.

---

## 5. Connecting your messaging channels

Go to **Settings → Channels**. This is where you connect WhatsApp, Instagram, Messenger, and your
website's chat widget so their messages start flowing into your Inbox.

### WhatsApp, Instagram, Messenger

Each of these needs two pieces of information from that platform's own business account (your IT
person or the platform's own setup guide can help you find these — they come from Meta's Business
Manager, not from Omnichannel):

1. **External account ID** — the platform's own identifier for your business account (for
   example, WhatsApp calls this a "phone number ID"). Paste it in and click **Save account ID**.
2. **Access token / secret** — a private key the platform gives you that lets Omnichannel send and
   receive messages on your behalf. Paste it in and click **Save credential**. Once saved, it's
   **encrypted and never shown again** — if you need to change it later, just paste a new one.

Once both are saved, the badge at the top of that channel's card changes to **Connected**. To
disconnect, click **Remove credential**.

### Website Chat (the chat box on your own website)

1. Under **Allowed embed origins**, type the web address(es) where you want the chat box to
   appear — one per line (for example `https://yourshop.com`). This is a safety measure: the chat
   box will only load on sites you explicitly allow.
2. Click **Save origins**.
3. Copy the **Embed snippet** shown below it, and paste it into your website's HTML (ask whoever
   manages your website to do this if you're not comfortable editing website code).

Once it's on your site, visitors can start chatting with you right from your website, and those
conversations show up in your Inbox exactly like WhatsApp or Instagram messages.

---

## 6. Teaching the AI about your business (Knowledge Base)

Go to **Settings → Knowledge Base**. This is where you paste in information about your business —
your policies, prices, FAQs, shipping times, whatever your customers commonly ask about — so the
AI can answer accurately from *your real information* instead of guessing.

1. Click **+ New document**.
2. Give it a title (e.g. "Shipping Policy") and paste in the content.
3. Save it.

The AI will search these documents when drafting a suggestion or an automatic reply, and will say
so honestly if it can't find an answer in what you've given it — it's built to never make things
up.

---

## 7. Letting AI help you reply

There are two independent AI features, and both are **off until you turn them on**:

### Suggest mode

The AI drafts a reply for you to read, edit if needed, and send yourself — it never sends anything
without you clicking Send. In any conversation, click the **✨ Suggest** button above the reply box.

### Auto-Reply mode

The AI can reply to customers **completely on its own**, but only when *all* of these are true:

1. You've turned it on for your whole business in **Settings → AI Auto-Reply**.
2. You've set that specific conversation's **AI mode** (in the conversation view) to
   `AutoReply` or `AutoReplyWithEscalation`.
3. It's currently within the **business hours** you've configured for AI auto-reply (unconfigured
   hours mean it never auto-replies — nothing happens by accident).
4. The AI is confident enough in its answer (you control how confident it needs to be with the
   **Confidence threshold** slider — higher means more cautious).
5. You haven't hit your **Daily auto-reply limit** yet.

Whenever the AI isn't confident, or the question is something sensitive (a refund, a complaint,
anything it can't answer from your Knowledge Base), it **always hands off to a human** instead of
guessing — if you've chosen `AutoReplyWithEscalation` mode, that conversation is automatically
flagged as Escalated so your team notices it needs attention.

### Choosing your own AI provider

By default, your account uses the platform's own AI. If you'd rather use your own AI account
(OpenAI, Anthropic/Claude, Groq, or basically any other major AI provider), go to
**Settings → AI Provider**:

1. Pick a preset from the **Preset** dropdown (or choose "Custom" for any other provider).
2. Paste your **API key** (get this from your AI provider's own website — it's like a password
   that lets Omnichannel use your account instead of the shared default).
3. Click **Auto-detect model** — Omnichannel will contact your provider, find out which AI models
   your key can use, and fill in the right settings for you automatically. You can still change
   anything it fills in.
4. Click **Save**.
5. Click **Test connection** to confirm it actually works before relying on it — you'll see a
   clear "Connected successfully" or an explanation of what went wrong.

Your API key is encrypted the moment you save it and is never shown again, even to you — if you
need to change it, just paste a new one.

---

## 8. Automation Rules

Go to **Settings → Automation Rules**. These let you say: *"whenever a customer message contains
this word, do this automatically"* — no AI needed, just a simple, predictable rule.

1. Click **+ New rule**.
2. Type a **trigger keyword** (e.g. "refund") — the rule fires whenever that word appears anywhere
   in an incoming customer message.
3. Choose at least one action:
   - **Apply tag** — automatically labels the conversation.
   - **Set priority** — automatically marks it as more or less urgent.
   - **Escalate** — flags the conversation for a human right away.
4. Click **Create rule**.

You can turn any rule on/off, or delete it, from the same screen.

---

## 9. Business Hours

Go to **Settings → Business Hours**. Set your normal weekly opening hours and mark specific
holiday dates as fully closed. This is separate from the AI-specific hours in
**Settings → AI Auto-Reply** — you might, for example, want the AI to only ever reply during
narrower hours than your team's own general hours, or vice versa.

---

## 10. Saved Replies

Go to **Settings → Saved Replies**. Create short, reusable answers to questions you get all the
time ("What are your hours?", "Where's my order?"). Give each one a title and the message text.
They then appear in the **Insert…** dropdown in every conversation's reply box, ready to drop in
and personalize before sending.

---

## 11. Email settings (SMTP)

Go to **Settings → Email (SMTP)**. This controls the email address transactional messages come
from — password resets, escalation alerts, and anything else the app emails your team.

By default, your business uses the platform's own shared email sender, shown as **"Using platform
default."** If you'd rather send from your own business email address:

1. Pick your provider from the **Provider** dropdown (Gmail, Outlook, Yahoo, and others are
   listed) — this fills in the technical Host/Port fields for you automatically. You can still
   edit them.
2. Fill in your **Username** (usually your full email address) and **App password** — most email
   providers require a special "app password" rather than your normal login password; your email
   provider's own help pages explain how to generate one (search "[your email provider] app
   password").
3. Fill in the **From address** and, optionally, a **From name**.
4. Click **Save**.
5. Click **Send test email** to confirm it actually works — a real test email will be sent to your
   own account so you can verify it arrived.

Click **Clear (use platform default)** at any time to switch back to the shared default.

---

## 12. Analytics

Go to **Settings → Analytics**. A dashboard of how your inbox is performing: total conversations,
resolution rate, average response and resolution time, a breakdown by conversation status, how
much the AI is doing (suggestions made, auto-replies sent, and how confident it's been), and
breakdowns by channel and by team member. Switch between the last 7, 30, or 90 days at the top.

---

## 13. Notifications (the bell icon)

The bell icon in the top-right corner shows you important alerts — for example, when a
conversation gets escalated by an automation rule. Click it to see your recent notifications; click
one to jump straight to the conversation it's about.

---

## 14. Team roles — who can do what

Every teammate on your account has one of four roles:

| Role | Can do |
|---|---|
| **Owner** | Everything — including the one thing Admins can't: permanently deleting the whole business account. |
| **Admin** | Everything day-to-day: connect channels, configure AI, manage automation, view analytics, reply to customers. |
| **Agent** | The day-to-day support work: read and reply to conversations, assign them, use saved replies. Cannot change business-wide settings. |
| **Viewer** | Read-only access to conversations and analytics — for someone who needs visibility but shouldn't make changes. |

> **A note on adding teammates:** Omnichannel doesn't yet have a self-service "invite a teammate"
> button. If you need to add someone to your team, ask your technical administrator for help in
> the meantime.

---

## 15. Your account and your business account

Go to **Settings → Account**. There are two very different things you can delete here — read the
difference carefully before clicking either.

### Delete my account

This deletes **only you** — your login and personal profile. You're removed from every business
you're part of. **This cannot be undone.**

- If you're the only Owner of a business that still has other teammates in it, you'll be stopped
  and asked to either hand ownership to someone else first, or delete the whole business instead
  (see below) — otherwise no one would be left able to manage it.
- If you're the only person in your business entirely, deleting your account also schedules that
  now-empty business for deletion (see below), since there'd be no one left to use it.

### Delete this business

**Owners only.** This permanently deletes every conversation, contact, and setting for your
*entire* business — for everyone on the team, not just you.

- It doesn't happen immediately. You get a **14-day grace period** — click **Delete this
  business**, confirm, and you'll see exactly what date it's scheduled for.
- You can click **Cancel deletion** at any point during those 14 days to stop it and keep
  everything as it was.
- Once those 14 days pass, the deletion is **permanent and cannot be undone.**
- You'll receive an email confirming the scheduled date the moment you request it.

---

## 16. Staying safe

A few simple habits that matter a lot:

- **Never share your password** with anyone, including people claiming to be from support. No
  legitimate request ever needs your password.
- **Use a strong, unique password** — not one you use anywhere else.
- **Sign out** when using a shared or public computer.
- **Only paste real API keys and access tokens into Omnichannel's own Settings screens** — never
  into an email, chat message, or any other website. These are saved encrypted and are never shown
  back to you or anyone else once saved.
- If you ever suspect your account or a connected channel's access token has been exposed, remove
  the credential in **Settings → Channels** (or your AI/Email settings) immediately and replace it
  with a new one.

---

## 17. Troubleshooting / "It's not working"

**I signed up but never got a confirmation email.**
Your business likely hasn't configured email sending yet (or the platform default isn't set up).
This doesn't block you from using the app — you can sign in and use everything normally. Ask your
administrator to configure Settings → Email if you need email features (like password reset) to
work.

**I clicked "Test connection" / "Send test email" and it failed.**
The message shown tells you the actual reason (wrong password, wrong host/port, unreachable
provider, etc.) — check the value you entered against what your provider's website shows, and try
again. A failed test never affects your existing working setup, if you had one.

**A channel shows "Not connected" even after I saved my details.**
Double check the External account ID and Access token are exactly what your channel provider
(Meta, etc.) issued for that specific account — a typo or an expired token is the most common
cause. Re-paste and save again.

**I can't see a Settings menu item my colleague can see.**
Settings screens only appear for roles that are allowed to use them (see
[section 14](#14-team-roles--who-can-do-what)) — this is expected, not a bug.

**The AI isn't auto-replying even though I turned it on.**
Check, in order: is the tenant-wide switch on in Settings → AI Auto-Reply? Is *that specific
conversation's* AI mode set to Auto-Reply? Are you currently within the business hours you
configured for it? Have you hit your daily limit? All four have to be true.

**Still stuck?**
Ask whoever manages your Omnichannel account technically (your Owner/Admin, or your IT support) —
they'll have access to more detailed logs than this manual can cover.

---

## 18. Glossary — plain-English meaning of every term

| Term | Meaning |
|---|---|
| **Channel** | Any app your customers can message you through — WhatsApp, Instagram, Messenger, your website chat, or "Manual" (conversations you create yourself). |
| **Conversation** | One ongoing thread of messages with one customer. |
| **Tag** | A short label you can put on a conversation to organize/find it later. |
| **Status** | Where a conversation currently stands: Open, Pending, Escalated, Resolved, or Closed. |
| **Priority** | How urgent a conversation is. |
| **Assign** | Claiming a conversation as the one you (or a teammate) are personally handling. |
| **Internal note** | A note on a conversation that only your team can see — never the customer. |
| **AI Suggest mode** | The AI drafts a reply; a human decides whether to send it, as-is or edited. |
| **AI Auto-Reply mode** | The AI can send simple replies on its own, within limits you set, and always hands off anything it isn't confident about. |
| **Escalate / Escalated** | Flagging a conversation as needing a human's attention right away. |
| **Knowledge Base** | The documents you give the AI so it answers from your real business information. |
| **Automation Rule** | A simple "if this keyword appears, do this" rule — no AI involved. |
| **API key** | A private code a service (like an AI provider or email provider) gives you so an app can act on your behalf. Treat it like a password. |
| **SMTP** | The technical name for "how emails actually get sent" — you don't need to understand it, just fill in what your email provider tells you to. |
| **Owner / Admin / Agent / Viewer** | The four levels of access a teammate can have — see [section 14](#14-team-roles--who-can-do-what). |
| **Tenant / Business account** | Your whole company's data and settings in Omnichannel — everything under your business name. |
| **Grace period** | A waiting period (14 days for account deletion) before an irreversible action actually happens, so mistakes can be undone in time. |
