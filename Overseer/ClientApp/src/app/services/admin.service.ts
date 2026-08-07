import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserDto {
  id: string;
  userName: string;
  email: string;
  groups: GroupDto[];
}

export interface UsersResponse {
  rows: UserDto[];
  totalCount: number;
}

export interface GroupDto {
  id: number;
  displayName: string;
}

export interface SystemAiConfigDto {
  id: number;
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  maxInputTokens: number | null;
  maxOutputTokens: number | null;
  orderIndex: number;
  isEnabled: boolean;
  hasApiKey: boolean;
  isSystemWide: boolean;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
  apiKey?: string;
  note?: string | null;
}

export interface UserSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
}

export interface GroupSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
}

export interface AnalyticsUserRow {
  userId: string;
  userName: string;
  chatRequests: number;
  titleRequests: number;
  inputTokens: number;
  outputTokens: number;
}

export interface AnalyticsResponse {
  rows: AnalyticsUserRow[];
  totalCount: number;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);

  // Analytics
  getConfigAnalytics(configId: number, params: {
    startDate?: string; endDate?: string;
    mode?: string; usernameFilter?: string;
    page?: number; pageSize?: number;
  }): Observable<AnalyticsResponse> {
    const httpParams: any = {};
    if (params.startDate) httpParams.startDate = params.startDate;
    if (params.endDate) httpParams.endDate = params.endDate;
    if (params.mode) httpParams.mode = params.mode;
    if (params.usernameFilter) httpParams.usernameFilter = params.usernameFilter;
    if (params.page) httpParams.page = params.page;
    if (params.pageSize) httpParams.pageSize = params.pageSize;
    return this.http.get<AnalyticsResponse>(
      `/api/admin/systemconfigs/${configId}/analytics`,
      { params: httpParams }
    );
  }

  // Users
  getUsers(page: number = 1, pageSize: number = 10, usernameFilter: string = '', sortColumn: string = 'UserName', sortOrder: string = 'asc'): Observable<UsersResponse> {
    const params: any = { page, pageSize, sortColumn, sortOrder };
    if (usernameFilter) {
      params.usernameFilter = usernameFilter;
    }
    return this.http.get<UsersResponse>('/api/admin/users', { params });
  }

  // Groups
  getGroups(): Observable<GroupDto[]> {
    return this.http.get<GroupDto[]>('/api/admin/groups');
  }
  
  createGroup(displayName: string): Observable<GroupDto> {
    return this.http.post<GroupDto>('/api/admin/groups', { displayName });
  }

  deleteGroup(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/groups/${id}`);
  }

  // User Groups
  addUserToGroup(userId: string, groupId: number): Observable<void> {
    return this.http.post<void>(`/api/admin/users/${userId}/groups`, { groupId });
  }

  removeUserFromGroup(userId: string, groupId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/users/${userId}/groups/${groupId}`);
  }

  // System AI Configs
  getSystemConfigs(): Observable<SystemAiConfigDto[]> {
    return this.http.get<SystemAiConfigDto[]>('/api/admin/systemconfigs');
  }

  createSystemConfig(config: any): Observable<SystemAiConfigDto> {
    return this.http.post<SystemAiConfigDto>('/api/admin/systemconfigs', config);
  }

  updateSystemConfig(id: number, config: any): Observable<SystemAiConfigDto> {
    return this.http.put<SystemAiConfigDto>(`/api/admin/systemconfigs/${id}`, config);
  }

  deleteSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/systemconfigs/${id}`);
  }

  resetSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/systemconfigs/${id}/reset`, { counterName });
  }

  reorderSystemConfigs(ids: number[]): Observable<void> {
    return this.http.put<void>('/api/admin/systemconfigs/reorder', ids);
  }

  // User System AI Configs
  getUserSystemConfigs(userId: string): Observable<UserSystemAiConfigDto[]> {
    return this.http.get<UserSystemAiConfigDto[]>(`/api/admin/users/${userId}/systemconfigs`);
  }

  createUserSystemConfig(userId: string, config: any): Observable<UserSystemAiConfigDto> {
    return this.http.post<UserSystemAiConfigDto>(`/api/admin/users/${userId}/systemconfigs`, config);
  }

  deleteUserSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/user-systemconfigs/${id}`);
  }

  updateUserSystemConfig(id: number, config: any): Observable<void> {
    return this.http.put<void>(`/api/admin/user-systemconfigs/${id}`, config);
  }

  resetUserSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/user-systemconfigs/${id}/reset`, { counterName });
  }

  reorderUserSystemConfigs(userId: string, ids: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/users/${userId}/systemconfigs/reorder`, ids);
  }

  // Group System AI Configs
  getGroupSystemConfigs(groupId: number): Observable<GroupSystemAiConfigDto[]> {
    return this.http.get<GroupSystemAiConfigDto[]>(`/api/admin/groups/${groupId}/systemconfigs`);
  }

  createGroupSystemConfig(groupId: number, config: any): Observable<GroupSystemAiConfigDto> {
    return this.http.post<GroupSystemAiConfigDto>(`/api/admin/groups/${groupId}/systemconfigs`, config);
  }

  deleteGroupSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/group-systemconfigs/${id}`);
  }

  updateGroupSystemConfig(id: number, config: any): Observable<void> {
    return this.http.put<void>(`/api/admin/group-systemconfigs/${id}`, config);
  }

  resetGroupSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/group-systemconfigs/${id}/reset`, { counterName });
  }

  reorderGroupSystemConfigs(groupId: number, ids: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/groups/${groupId}/systemconfigs/reorder`, ids);
  }
}
