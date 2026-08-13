import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApplyCs2ModePresetRequest, ConsoleCommandResult, CreateServerRequest, Cs2ModeApplyResult, Cs2ModeCatalog, Cs2ModeState, GameServer, GameTemplate, HostReadinessSnapshot, HostSnapshot, PublicServerList, ServerEvent, ServerPublication, ServerSelfTestResult } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  authStatus(): Observable<{ setupRequired: boolean }> {
    return this.http.get<{ setupRequired: boolean }>('/api/auth/status');
  }

  me(): Observable<{ userName: string }> {
    return this.http.get<{ userName: string }>('/api/auth/me');
  }

  authenticate(setup: boolean, userName: string, password: string): Observable<{ userName: string }> {
    const action = setup ? 'bootstrap' : 'login';
    return this.http.post<{ userName: string }>(`/api/auth/${action}`, { userName, password });
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {});
  }

  host(): Observable<HostSnapshot> {
    return this.http.get<HostSnapshot>('/api/host');
  }

  hostReadiness(): Observable<HostReadinessSnapshot> {
    return this.http.get<HostReadinessSnapshot>('/api/host/readiness');
  }

  templates(): Observable<GameTemplate[]> {
    return this.http.get<GameTemplate[]>('/api/game-templates');
  }

  servers(): Observable<GameServer[]> {
    return this.http.get<GameServer[]>('/api/servers');
  }

  server(id: string): Observable<GameServer> {
    return this.http.get<GameServer>(`/api/servers/${id}`);
  }

  createServer(request: CreateServerRequest): Observable<GameServer> {
    return this.http.post<GameServer>('/api/servers', request);
  }

  serverAction(id: string, action: 'start' | 'stop' | 'restart' | 'kill' | 'update'): Observable<unknown> {
    return this.http.post(`/api/servers/${id}/${action}`, {});
  }

  deleteServer(id: string, deleteFiles = true): Observable<void> {
    return this.http.delete<void>(`/api/servers/${id}`, { params: { deleteFiles } });
  }

  updatePublication(id: string, published: boolean, publicPort: number): Observable<ServerPublication> {
    return this.http.put<ServerPublication>(`/api/servers/${id}/publication`, { published, publicPort });
  }

  publicServers(): Observable<PublicServerList> {
    return this.http.get<PublicServerList>('/api/public/servers');
  }

  cs2ModeCatalog(): Observable<Cs2ModeCatalog> {
    return this.http.get<Cs2ModeCatalog>('/api/cs2/mode-presets');
  }

  cs2Mode(id: string): Observable<Cs2ModeState> {
    return this.http.get<Cs2ModeState>(`/api/servers/${id}/cs2-mode`);
  }

  applyCs2Mode(id: string, request: ApplyCs2ModePresetRequest): Observable<Cs2ModeApplyResult> {
    return this.http.put<Cs2ModeApplyResult>(`/api/servers/${id}/cs2-mode`, request);
  }

  installCs2Package(id: string, packageId: string): Observable<void> {
    return this.http.post<void>(`/api/servers/${id}/cs2-packages/${packageId}/install`, {});
  }

  sendCommand(id: string, command: string): Observable<ConsoleCommandResult> {
    return this.http.post<ConsoleCommandResult>(`/api/servers/${id}/command`, { command });
  }

  selfTest(id: string): Observable<ServerSelfTestResult> {
    return this.http.post<ServerSelfTestResult>(`/api/servers/${id}/self-test`, {});
  }

  logs(id: string, take = 300): Observable<ServerEvent[]> {
    return this.http.get<ServerEvent[]>(`/api/servers/${id}/logs`, { params: { take } });
  }

  activity(take = 100): Observable<ServerEvent[]> {
    return this.http.get<ServerEvent[]>('/api/activity', { params: { take } });
  }
}
