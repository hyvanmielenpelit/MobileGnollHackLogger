import { RouteReuseStrategy, ActivatedRouteSnapshot, DetachedRouteHandle } from '@angular/router';

export class CustomRouteReuseStrategy implements RouteReuseStrategy {
  private handlers: { [key: string]: DetachedRouteHandle } = {};

  shouldDetach(route: ActivatedRouteSnapshot): boolean {
    // Only detach and reuse routes that explicitly specify data: { reuse: true }
    return route.data && route.data['reuse'] === true;
  }

  store(route: ActivatedRouteSnapshot, handle: DetachedRouteHandle): void {
    if (route.routeConfig && route.routeConfig.path) {
      this.handlers[route.routeConfig.path] = handle;
    }
  }

  shouldAttach(route: ActivatedRouteSnapshot): boolean {
    return !!route.routeConfig && !!route.routeConfig.path && !!this.handlers[route.routeConfig.path];
  }

  retrieve(route: ActivatedRouteSnapshot): DetachedRouteHandle | null {
    if (!route.routeConfig || !route.routeConfig.path) return null;
    return this.handlers[route.routeConfig.path] || null;
  }

  shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    // Default Angular behavior: reuse route if the route config is the same
    return future.routeConfig === curr.routeConfig;
  }
}
