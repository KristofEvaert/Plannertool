import { inject, Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '@environments/environment';
import type { RouteMessageDto } from '@models';
import { AuthService } from '@services/auth.service';

@Injectable({ providedIn: 'root' })
export class RouteMessagesHubService {
  private readonly auth = inject(AuthService);
  private connection: signalR.HubConnection | null = null;
  private readonly messageSubject = new Subject<RouteMessageDto>();
  readonly messages$ = this.messageSubject.asObservable();

  connect(): void {
    if (this.connection) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/route-messages`, {
        accessTokenFactory: () => this.auth.getToken() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('routeMessageCreated', (message: RouteMessageDto) => {
      this.messageSubject.next(message);
    });

    this.connection.start().catch(() => {
      // Swallow connection errors; UI will continue to poll.
    });
  }

  disconnect(): void {
    if (!this.connection) {
      return;
    }
    this.connection.stop();
    this.connection = null;
  }
}
