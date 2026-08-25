import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { Store } from '@ngrx/store';
import { filter, map } from 'rxjs';
import { AuthActions } from '../../../core/auth/store/auth.actions';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { selectCartItemCount } from '../../../core/cart/store/cart.selectors';
import { CategoryService } from '../../../core/services/category';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss',
})
export class Navbar {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  private readonly categoryService = inject(CategoryService);

  readonly user = this.store.selectSignal(selectAuthUser);
  readonly cartCount = this.store.selectSignal(selectCartItemCount);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );
  readonly showSearch = computed(
    () => !this.url().startsWith('/login') && !this.url().startsWith('/register'),
  );

  readonly searchQuery = signal('');
  readonly searchCategoryId = signal('');
  readonly categories = signal<{ id: string; name: string }[]>([]);

  // The applied search term — set on submit, cleared on cancel. The button
  // only shows as "clear" while the input still matches what was actually
  // searched for; editing the text after a search reverts it to "search".
  private readonly appliedQuery = signal('');
  readonly isSearchActive = computed(
    () => this.appliedQuery() !== '' && this.appliedQuery() === this.searchQuery().trim(),
  );

  constructor() {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.categories.set([]),
    });
  }

  // Client-side filter on the catalog page — there's no search API, so this
  // just navigates there with ?q=/?categoryId= and the catalog page's own
  // signals filter the already-loaded product list.
  submitSearch(): void {
    const query = this.searchQuery().trim();
    this.appliedQuery.set(query);
    this.router.navigate(['/customer/catalog'], {
      queryParams: { q: query || null, categoryId: this.searchCategoryId() || null },
    });
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.appliedQuery.set('');
    this.router.navigate(['/customer/catalog'], {
      queryParams: { q: null, categoryId: this.searchCategoryId() || null },
    });
  }

  onSearchButtonClick(): void {
    if (this.isSearchActive()) {
      this.clearSearch();
    } else {
      this.submitSearch();
    }
  }

  initials(displayName: string): string {
    return displayName
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }

  logout(): void {
    this.store.dispatch(AuthActions.logout());
  }
}
