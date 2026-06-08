import { sliceClient, setSliceAuthToken } from '../services/sliceHttpClient';
import type { SliceLoginResponse, SliceUserInfo } from '../slice/types';

export { setSliceAuthToken };

export async function sliceGoogleLogin(accessToken: string): Promise<SliceLoginResponse> {
  const { data } = await sliceClient.post<SliceLoginResponse>('/auth/google', { accessToken });
  return data;
}

export async function sliceLogin(email: string, password: string): Promise<SliceLoginResponse> {
  const { data } = await sliceClient.post<SliceLoginResponse>('/auth/login', { email, password });
  return data;
}

export async function sliceGetMe(): Promise<SliceUserInfo> {
  const { data } = await sliceClient.get<SliceUserInfo>('/auth/me');
  return data;
}
