import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';

import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AuthService } from '@services/auth.service';

interface MapMarker {
  top: string;
  left: string;
  color: string;
  ringColor: string;
}

@Component({
  selector: 'app-start',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CardModule, ButtonModule, RouterLink],
  templateUrl: './start.page.html',
  standalone: true,
})
export class StartPage implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);

  activePingIndex = signal<number>(0);
  private pingInterval: any;

  // Show admin buttons if user has any role except when they only have the Driver role
  showButtons = computed(() => {
    const user = this.auth.currentUser();
    if (!user || !user.roles || user.roles.length === 0) {
      return false;
    }
    // Don't show if the only role is Driver
    if (user.roles.length === 1 && user.roles[0] === 'Driver') {
      return false;
    }
    return true;
  });

  readonly markers: MapMarker[] = [
    { top: '20%', left: '25%', color: 'bg-red-500', ringColor: 'ring-red-500/30' },
    { top: '35%', left: '45%', color: 'bg-blue-500', ringColor: 'ring-blue-500/30' },
    { top: '50%', left: '30%', color: 'bg-green-500', ringColor: 'ring-green-500/30' },
    { top: '60%', left: '55%', color: 'bg-yellow-500', ringColor: 'ring-yellow-500/30' },
    { top: '25%', left: '65%', color: 'bg-purple-500', ringColor: 'ring-purple-500/30' },
    { top: '70%', left: '40%', color: 'bg-orange-500', ringColor: 'ring-orange-500/30' },
    { top: '45%', left: '70%', color: 'bg-pink-500', ringColor: 'ring-pink-500/30' },
    { top: '75%', left: '60%', color: 'bg-teal-500', ringColor: 'ring-teal-500/30' },
  ];

  ngOnInit() {
    this.pingInterval = setInterval(() => {
      const randomIndex = Math.floor(Math.random() * this.markers.length);
      this.activePingIndex.set(randomIndex);
    }, 1000);
  }

  ngOnDestroy() {
    if (this.pingInterval) {
      clearInterval(this.pingInterval);
    }
  }
}
