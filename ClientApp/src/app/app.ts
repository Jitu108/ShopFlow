import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Navbar } from './shared/components/navbar/navbar';
import { VerifyEmailBanner } from './shared/components/verify-email-banner/verify-email-banner';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Navbar, VerifyEmailBanner],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
