import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserDto {
  id: string;
  userName: string;
  email: string;
  groups: GroupDto[];
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
  isSystemWide: boolean;
  maxDailyRequests: number | null;
  maxMonthlyRequests: number | null;
  maxTotalRequests: number | null;
  apiKey?: string;
}

export interface UserSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyRequests: number | null;
  maxMonthlyRequests: number | null;
  maxTotalRequests: number | null;
}

export interface GroupSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyRequests: number | null;
  maxMonthlyRequests: number | null;
  maxTotalRequests: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);

  // Users
  getUsers(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>('/api/admin/users');
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

  reorderGroupSystemConfigs(groupId: number, ids: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/groups/${groupId}/systemconfigs/reorder`, ids);
  }
}
