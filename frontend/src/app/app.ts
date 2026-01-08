import { Component, inject, OnInit, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MANUAL_SECTIONS } from '@app/_data/manual.data';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import { NavbarComponent } from '@components/navbar/navbar.component';
import { PrimeNG } from 'primeng/config';
import { ToastModule } from 'primeng/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule, NavbarComponent, HelpManualComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  public help = viewChild<HelpManualComponent>('help');
  private config = inject(PrimeNG);

  manualSections = MANUAL_SECTIONS;

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

  toggleHelp() {
    this.help()!.open();
  }
}
