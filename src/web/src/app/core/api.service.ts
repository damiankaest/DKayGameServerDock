import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateServerRequest, GameServer, GameTemplate, HostReadinessSnapshot, HostSnapshot, PublicServerList, ServerEvent, ServerPublication } from './models';

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

  updatePublication(id: string, published: boolean, publicPort: number): Observable<ServerPublication> {
    return this.http.put<ServerPublication>(`/api/servers/${id}/publication`, { published, publicPort });
  }

  publicServers(): Observable<PublicServerList> {
    return this.http.get<PublicServerList>('/api/public/servers');
  }

  sendCommand(id: string, command: string): Observable<void> {
    return this.http.post<void>(`/api/servers/${id}/command`, { command });
  }

  logs(id: string, take = 300): Observable<ServerEvent[]> {
    return this.http.get<ServerEvent[]>(`/api/servers/${id}/logs`, { params: { take } });
  }

  activity(take = 100): Observable<ServerEvent[]> {
    return this.http.get<ServerEvent[]>('/api/activity', { params: { take } });
  }
}
