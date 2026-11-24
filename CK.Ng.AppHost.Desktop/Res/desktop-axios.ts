import axios, { AxiosAdapter, AxiosError, AxiosHeaders, AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from 'axios';

/**
 * The envelope id of the requests and of the responses (DesktopInProcessBridge of CK.AppHost.Desktop).
 */
const messageId = 'ck:http';

type BridgeResponse = { id: number, status: number, headers: Record<string, string>, body: string, base64: boolean };

const pending = new Map<number, ( r: BridgeResponse ) => void>();
let nextId = 0;
let listening = false;

function onHostMessage( data: unknown ): void {
  let e: any;
  try { e = typeof data === 'string' ? JSON.parse( data ) : data; } catch { return; }
  if( e?.id !== messageId || typeof e.data !== 'string' ) return;
  const r = JSON.parse( e.data ) as BridgeResponse;
  const resolve = pending.get( r.id );
  if( resolve ) {
    pending.delete( r.id );
    resolve( r );
  }
}

function listen(): void {
  if( listening ) return;
  listening = true;
  const w = window as any;
  // The script that InfiniFrame injects in the page dispatches the host messages too, and warns for an unknown id: an
  // empty handler declares this one. The responses are read below (InfiniFrame's dispatcher caps a message at 1 MiB).
  w.infiniframe?.messaging?.assignMessageReceivedHandler?.( messageId, () => {} );
  if( w.chrome?.webview?.addEventListener ) {
    // WebView2 (Windows).
    w.chrome.webview.addEventListener( 'message', ( ev: any ) => onHostMessage( ev.data ) );
  }
  else {
    // WebKit (macOS, Linux): InfiniFrame dispatches the host messages to window.__infiniframe_dispatch.
    const previous = w.__infiniframe_dispatch;
    w.__infiniframe_dispatch = ( m: unknown ) => { onHostMessage( m ); previous?.( m ); };
  }
}

function postToHost( text: string ): void {
  const w = window as any;
  if( w.chrome?.webview?.postMessage ) w.chrome.webview.postMessage( text );
  else if( w.webkit?.messageHandlers?.infiniFrameInterop?.postMessage ) w.webkit.messageHandlers.infiniFrameInterop.postMessage( text );
  else throw new Error( 'The desktop host is not available.' );
}

function toArrayBuffer( base64: string ): ArrayBuffer {
  const s = atob( base64 );
  const bytes = new Uint8Array( s.length );
  for( let i = 0; i < s.length; ++i ) bytes[i] = s.charCodeAt( i );
  return bytes.buffer;
}

/**
 * The axios adapter of a page served by the "app" scheme of a desktop window: each request is a message to the host
 * that runs it on its in-process endpoint (no port is opened). The bodies are text (JSON): a request with another body
 * (FormData, binary) is refused.
 */
export const desktopAxiosAdapter: AxiosAdapter = ( config: InternalAxiosRequestConfig ) => {
  listen();
  const url = new URL( axios.getUri( config ), location.href );
  if( config.data != null && typeof config.data !== 'string' ) {
    return Promise.reject( new AxiosError( 'Only text bodies can be sent to the desktop host.', AxiosError.ERR_BAD_REQUEST, config ) );
  }
  const id = ++nextId;
  const headers: Record<string, string> = {};
  for( const [k, v] of Object.entries( AxiosHeaders.from( config.headers as any ).toJSON() ) ) {
    if( v != null ) headers[k] = Array.isArray( v ) ? v.join( ', ' ) : String( v );
  }
  return new Promise<AxiosResponse>( ( resolve, reject ) => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    if( config.timeout ) {
      timer = setTimeout( () => {
        pending.delete( id );
        reject( new AxiosError( `timeout of ${config.timeout}ms exceeded`, AxiosError.ECONNABORTED, config ) );
      }, config.timeout );
    }
    pending.set( id, r => {
      if( timer ) clearTimeout( timer );
      const response: AxiosResponse = {
        data: r.base64 ? toArrayBuffer( r.body ) : r.body,
        status: r.status,
        statusText: '',
        headers: AxiosHeaders.from( r.headers ),
        config,
        request: null
      };
      const validate = config.validateStatus;
      if( !validate || validate( r.status ) ) resolve( response );
      else {
        reject( new AxiosError( `Request failed with status code ${r.status}`,
                                r.status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST,
                                config,
                                null,
                                response ) );
      }
    } );
    try {
      const request = { id, method: ( config.method ?? 'get' ).toUpperCase(), url: url.pathname + url.search, headers, body: config.data ?? null };
      postToHost( JSON.stringify( { id: messageId, command: 'Post', version: 2, data: JSON.stringify( request ) } ) );
    }
    catch( e ) {
      if( timer ) clearTimeout( timer );
      pending.delete( id );
      reject( e );
    }
  } );
};

/**
 * Creates the axios instance of the application: when the page is served by the "app" scheme of a desktop window (an
 * in-process endpoint), its requests are messages to the host (desktopAxiosAdapter).
 */
export function createDesktopAxios(): AxiosInstance {
  const instance = axios.create();
  if( location.protocol === 'app:' ) instance.defaults.adapter = desktopAxiosAdapter;
  return instance;
}
