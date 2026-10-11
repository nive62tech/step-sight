const KEY = 'desktop-guide-pkce';
const random = () => btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32)))).replaceAll('+','-').replaceAll('/','_').replaceAll('=','');
export class Auth {
  #config; #access = ''; #refresh = ''; #expires = 0; #epoch = 0; #refreshing;
  constructor(config) { this.#config = config; }
  get signedIn() { return Boolean(this.#access && (this.#expires > Date.now() || this.#refresh)); }
  async signIn() {
    if (!this.#config.configured || !/^https:\/\/[a-z0-9-]+\.auth\.[a-z0-9-]+\.amazoncognito\.com$/.test(this.#config.domain)) throw Error('Sign-in is not configured.');
    const verifier = random(), state = random();
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
    const challenge = btoa(String.fromCharCode(...new Uint8Array(digest))).replaceAll('+','-').replaceAll('/','_').replaceAll('=','');
    sessionStorage.setItem(KEY, JSON.stringify({verifier,state,created:Date.now()}));
    const params = new URLSearchParams({client_id:this.#config.client_id,response_type:'code',redirect_uri:this.callback,
      scope:this.#config.scope,state,code_challenge:challenge,code_challenge_method:'S256'});
    location.assign(`${this.#config.domain}/oauth2/authorize?${params}`);
  }
  get callback() { return `${location.origin}/control/`; }
  async complete() {
    const query = new URLSearchParams(location.search);
    if (!query.has('code') && !query.has('error')) return;
    history.replaceState(null,'','/control/');
    const raw = sessionStorage.getItem(KEY); sessionStorage.removeItem(KEY);
    if (!raw) throw Error('This sign-in has expired. Please sign in again.');
    const saved = JSON.parse(raw);
    if (!saved.state || query.get('state') !== saved.state || Date.now()-saved.created > 600000 || Date.now() < saved.created) throw Error('Sign-in could not be verified. Please try again.');
    if (query.has('error')) throw Error('Sign-in was cancelled or could not be completed.');
    await this.#tokens({grant_type:'authorization_code',code:query.get('code'),redirect_uri:this.callback,code_verifier:saved.verifier});
  }
  async #tokens(values) {
    const epoch = this.#epoch;
    const response = await fetch(`${this.#config.domain}/oauth2/token`, {method:'POST',cache:'no-store',signal:AbortSignal.timeout(15000),
      headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({client_id:this.#config.client_id,...values})});
    if (!response.ok) throw Error('Sign-in expired or could not be completed. Please sign in again.');
    const value = await response.json();
    if (epoch !== this.#epoch) throw Error('Signed out.');
    if (value.token_type !== 'Bearer' || typeof value.access_token !== 'string' || !Number.isFinite(value.expires_in) || value.expires_in <= 0) throw Error('Invalid sign-in response.');
    this.#access = value.access_token; this.#expires = Date.now()+value.expires_in*1000;
    if (value.refresh_token) this.#refresh = value.refresh_token;
  }
  async access() {
    if (!this.signedIn) throw Error('Sign in to connect your computer.');
    if (this.#expires < Date.now()+60000 && this.#refresh) {
      this.#refreshing ??= this.#tokens({grant_type:'refresh_token',refresh_token:this.#refresh}).finally(() => { this.#refreshing = null; });
      await this.#refreshing;
    }
    return this.#access;
  }
  async signOut() {
    const refresh = this.#refresh; this.#epoch++; this.#access = this.#refresh = ''; this.#expires = 0; sessionStorage.removeItem(KEY);
    if (refresh) {
      try { await fetch(`${this.#config.domain}/oauth2/revoke`,{method:'POST',signal:AbortSignal.timeout(4000),headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({client_id:this.#config.client_id,token:refresh})}); } catch {}
    }
    location.assign(`${this.#config.domain}/logout?${new URLSearchParams({client_id:this.#config.client_id,logout_uri:this.callback})}`);
  }
}
