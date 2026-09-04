export interface EmailSettingsResponse {
  host: string | null;
  port: number;
  username: string | null;
  fromAddress: string | null;
  fromName: string | null;
  isConfigured: boolean;
  hasPassword: boolean;
}

export interface UpdateEmailSettingsRequest {
  host: string;
  port: number;
  username: string;
  fromAddress: string;
  fromName: string | null;
  password: string | null;
}

export interface EmailTestResponse {
  success: boolean;
  message: string;
}

export interface SmtpProviderPreset {
  label: string;
  host: string;
  port: number;
}

/** Auto-fills host/port for common providers — fields stay editable afterward. Not exhaustive:
 * "Custom" lets a user enter any other SMTP server by hand. */
export const SMTP_PROVIDER_PRESETS: SmtpProviderPreset[] = [
  { label: 'Custom', host: '', port: 587 },
  { label: 'Gmail', host: 'smtp.gmail.com', port: 587 },
  { label: 'Outlook / Microsoft 365', host: 'smtp.office365.com', port: 587 },
  { label: 'Yahoo Mail', host: 'smtp.mail.yahoo.com', port: 587 },
  { label: 'Zoho Mail', host: 'smtp.zoho.com', port: 587 },
  { label: 'SendGrid', host: 'smtp.sendgrid.net', port: 587 },
  { label: 'Amazon SES (US East 1)', host: 'email-smtp.us-east-1.amazonaws.com', port: 587 },
  { label: 'Mailgun', host: 'smtp.mailgun.org', port: 587 },
];
