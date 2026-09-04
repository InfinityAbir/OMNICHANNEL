export interface AiSuggestionResponse {
  id: string;
  suggestedText: string;
  confidence: number;
  createdAt: string;
}

export interface BusinessHoursWindow {
  start: string;
  end: string;
}

export type BusinessHoursSchedule = Partial<Record<DayOfWeekName, BusinessHoursWindow[]>>;

export type DayOfWeekName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

export const DAYS_OF_WEEK: DayOfWeekName[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];

export interface AiAutoReplySettingsResponse {
  enabled: boolean;
  confidenceThreshold: number;
  dailyLimit: number;
  businessHours: BusinessHoursSchedule;
}

export interface UpdateAiAutoReplySettingsRequest {
  enabled: boolean;
  confidenceThreshold: number;
  dailyLimit: number;
  businessHours: BusinessHoursSchedule | null;
}
