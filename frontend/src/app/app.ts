import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PrimeNG } from 'primeng/config';
import { ToastModule } from 'primeng/toast';
import { NavbarComponent } from '@components/navbar/navbar.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule, NavbarComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  private config = inject(PrimeNG);

  ngOnInit(): void {
    const darkMode = localStorage.getItem('darkMode');
    const element = document.querySelector('html');
    if (darkMode === 'true') {
      element?.classList.add('dark');
    } else {
      element?.classList.remove('dark');
    }

    this.config.setTranslation({
      dateFormat: 'yy-mm-dd',
    });
  }
}
