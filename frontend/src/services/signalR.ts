import * as signalR from '@microsoft/signalr';
import type { DashboardSummaryDto } from '../types';

export class DashboardHubConnection {
  private connection: signalR.HubConnection;

  constructor(
    private onUpdate: (data: DashboardSummaryDto) => void,
    private onError: (message: string) => void
  ) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/dashboard')
      .withAutomaticReconnect()
      .build();

    this.connection.on('BroadcastDashboardUpdate', (data: DashboardSummaryDto) => {
      this.onUpdate(data);
    });

    this.connection.on('NotifyError', (message: string) => {
      this.onError(message);
    });
  }

  async start(): Promise<void> {
    await this.connection.start();
  }

  async stop(): Promise<void> {
    await this.connection.stop();
  }

  async requestUpdate(period: string): Promise<void> {
    await this.connection.invoke('RequestDashboardUpdate', period);
  }
}
