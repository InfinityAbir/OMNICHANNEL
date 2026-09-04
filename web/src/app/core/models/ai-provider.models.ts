export type AiProviderKind = 'OpenAiCompatible' | 'Anthropic';

export interface AiProviderSettingsResponse {
  providerKind: AiProviderKind;
  baseUrl: string | null;
  model: string;
  hasApiKey: boolean;
}

export interface UpdateAiProviderSettingsRequest {
  providerKind: AiProviderKind;
  baseUrl: string | null;
  model: string;
  apiKey: string | null;
}

export interface AiProviderTestResponse {
  success: boolean;
  message: string;
}

export interface DetectAiProviderRequest {
  apiKey: string;
  providerKind?: AiProviderKind;
  baseUrl?: string;
}

export interface DetectAiProviderResponse {
  success: boolean;
  message: string;
  providerKind: AiProviderKind;
  baseUrl: string | null;
  availableModels: string[];
  suggestedModel: string | null;
}

/** Presets to auto-fill provider kind + base URL from a familiar name — the user can always
 * switch to "Custom" for any other OpenAI-compatible endpoint (ADR-0027: "not just 3 providers"). */
export const AI_PROVIDER_PRESETS: { label: string; providerKind: AiProviderKind; baseUrl: string | null }[] = [
  { label: 'Groq', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.groq.com/openai/v1' },
  { label: 'OpenAI', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.openai.com/v1' },
  { label: 'Anthropic (Claude)', providerKind: 'Anthropic', baseUrl: null },
  { label: 'Together AI', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.together.xyz/v1' },
  { label: 'Fireworks AI', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.fireworks.ai/inference/v1' },
  { label: 'Mistral', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.mistral.ai/v1' },
  { label: 'DeepSeek', providerKind: 'OpenAiCompatible', baseUrl: 'https://api.deepseek.com/v1' },
  { label: 'OpenRouter', providerKind: 'OpenAiCompatible', baseUrl: 'https://openrouter.ai/api/v1' },
  { label: 'Custom (any OpenAI-compatible endpoint)', providerKind: 'OpenAiCompatible', baseUrl: '' },
];
