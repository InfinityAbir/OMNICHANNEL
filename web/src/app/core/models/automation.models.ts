import { BusinessHoursSchedule } from './ai.models';

export interface AutomationRuleResponse {
  id: string;
  name: string;
  enabled: boolean;
  keyword: string;
  applyTagName: string | null;
  setPriority: string | null;
  escalate: boolean;
  createdAt: string;
}

export interface CreateAutomationRuleRequest {
  name?: string;
  keyword: string;
  applyTagName?: string;
  setPriority?: string;
  escalate: boolean;
}

export interface SavedReplyResponse {
  id: string;
  title: string;
  text: string;
  createdAt: string;
  updatedAt: string;
}

export interface SavedReplyRequest {
  title: string;
  text: string;
}

export interface TenantBusinessHoursResponse {
  businessHours: BusinessHoursSchedule;
  holidays: string[];
}

export interface UpdateTenantBusinessHoursRequest {
  businessHours: BusinessHoursSchedule | null;
  holidays: string[] | null;
}
