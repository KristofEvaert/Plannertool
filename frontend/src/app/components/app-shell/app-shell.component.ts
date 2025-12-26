import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { environment } from '@environments/environment';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.css',
  standalone: true,
})
export class AppShellComponent {
  private readonly env = environment;
  environmentLabel = computed(() => {
    const url = this.env.apiBaseUrl;
    if (url.includes('localhost') || url.includes('127.0.0.1')) {
      return 'dev';
    }
    return 'prod';
  });
}

