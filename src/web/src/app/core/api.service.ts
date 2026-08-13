import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApplyCs2ModePresetRequest, ConfigureCs2GsltResult, ConfigureCs2WorkshopKeyResult, ConsoleCommandResult, CreateServerRequest, Cs2LiveConfigurationApplyResult, Cs2LiveControlState, Cs2MapChangeState, Cs2ModeApplyResult, Cs2ModeCatalog, Cs2ModeState, Cs2WorkshopSearchResult, GameServer, GameTemplate, HostReadinessSnapshot, HostSnapshot, PublicServerList, ServerEvent, ServerPublication, ServerSelfTestResult } from './models';

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

  searchCs2Workshop(id: string, query: string, take = 18): Observable<Cs2WorkshopSearchResult> {
    return this.http.get<Cs2WorkshopSearchResult>(`/api/servers/${id}/cs2-workshop/search`, { params: { query, take } });
  }

  configureCs2WorkshopKey(id: string, key: string): Observable<ConfigureCs2WorkshopKeyResult> {
    return this.http.put<ConfigureCs2WorkshopKeyResult>(`/api/servers/${id}/cs2-workshop/key`, { key });
  }

  cs2LiveControl(id: string): Observable<Cs2LiveControlState> {
    return this.http.get<Cs2LiveControlState>(`/api/servers/${id}/cs2-control`);
  }

  applyCs2LiveControl(id: string, values: Record<string, string>): Observable<Cs2LiveConfigurationApplyResult> {
    return this.http.put<Cs2LiveConfigurationApplyResult>(`/api/servers/${id}/cs2-control`, { values });
  }

  runCs2Action(id: string, actionId: string, value: string | null = null): Observable<ConsoleCommandResult> {
    return this.http.post<ConsoleCommandResult>(`/api/servers/${id}/cs2-control/actions`, { actionId, value });
  }

  cs2MapChange(id: string): Observable<Cs2MapChangeState> {
    return this.http.get<Cs2MapChangeState>(`/api/servers/${id}/cs2-control/map-change`);
  }

  scheduleCs2MapChange(id: string, profileId: string, delaySeconds: number): Observable<Cs2MapChangeState> {
    return this.http.post<Cs2MapChangeState>(`/api/servers/${id}/cs2-control/map-change`, { profileId, delaySeconds });
  }

  cancelCs2MapChange(id: string): Observable<Cs2MapChangeState> {
    return this.http.delete<Cs2MapChangeState>(`/api/servers/${id}/cs2-control/map-change`);
  }

  configureCs2Gslt(id: string, token: string): Observable<ConfigureCs2GsltResult> {
    return this.http.put<ConfigureCs2GsltResult>(`/api/servers/${id}/cs2-control/gslt`, { token });
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
