export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  businessName: string;
  timeZone?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
}

export interface CurrentUserResponse {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  tenantName: string;
  role: string;
  permissions: string[];
}
