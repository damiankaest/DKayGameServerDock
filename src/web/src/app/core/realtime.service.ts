import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private connection: HubConnection | null = null;

  async connect(
    serverId: string,
    handlers: {
      consoleLine: (line: { occurredAt: string; message: string; dataJson: string | null }) => void;
      statusChanged: (status: string) => void;
      installationProgress: (progress: { percent: number; stage: string; message: string }) => void;
    }
  ): Promise<void> {
    await this.disconnect();
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/servers')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('consoleLine', handlers.consoleLine);
    this.connection.on('statusChanged', handlers.statusChanged);
    this.connection.on('installationProgress', handlers.installationProgress);
    await this.connection.start();
    await this.connection.invoke('JoinServer', serverId);
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }
}

