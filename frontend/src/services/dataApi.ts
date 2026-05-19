import { client } from './httpClient';

export async function editRow(table: string, id: number, updates: Record<string, unknown>): Promise<void> {
  await client.put(`/data/${table}/edit/${id}`, { updates });
}

export async function deleteRow(table: string, id: number): Promise<void> {
  await client.delete(`/data/${table}/delete/${id}`);
}
