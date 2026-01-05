import { Component } from '@angular/core';
import { CardModule } from 'primeng/card';

@Component({
  selector: 'app-charging-posts',
  imports: [CardModule],
  template: `
    <div class="max-w-4xl mx-auto">
      <h1 class="text-3xl font-bold text-gray-900 mb-6">Charging Posts</h1>
      <p-card>
        <ng-template pTemplate="content">
          <p class="text-gray-600">Charging posts overview — coming soon</p>
        </ng-template>
      </p-card>
    </div>
  `,
  standalone: true,
})
export class ChargingPostsPage {}
