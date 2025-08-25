import grpc
from concurrent import futures
import main_pb2, main_pb2_grpc
from PIL import Image
import numpy as np
import io

class StegServicer(main_pb2_grpc.SteganographyServiceServicer):
    def Hide(self, request, context):
        try:
            img = Image.open(io.BytesIO(request.image))
            pixels = np.array(img)

            #LSB
            secret_bits = ''.join(format(byte, '08b') for byte in request.secret_encrypted)
            secret_bits += '00000000' #EOF

            idx = 0
            for i in range(pixels.shape[0]):
                for j in range(pixels.shape[1]):
                    for k in range(3):
                        if idx < len(secret_bits):
                            pixels[i][j][k] = (pixels[i][j][k] & 0xFE) | int(secret_bits[idx])
                            idx += 1
            output_img = Image.fromarray(pixels)
            img_byte_arr = io.BytesIO()
            output_img.save(img_byte_arr, format = 'PNG')
            return main_pb2.HideResponse(stego_image=img_byte_arr.getvalue())
        except Exception as e:
            return main_pb2.HideResponse(error=str(e))
    def Extract(self, request, context):
        try:
            print ('extraction')
            img = Image.open(io.BytesIO(request.stego_image))
            pixels = np.array(img)
            binary_data = ''

            #LSB extraction
            for i in range(pixels.shape[0]):
                for j in range(pixels.shape[1]):
                    for k in range(3):
                        binary_data += str(pixels[i][j][k] & 1)

            #EOF detection
            eof_index = binary_data.find('00000000')
            if eof_index == -1:
                raise ValueError("EOF not found")
            
            secret_bytes = bytes(int(binary_data[i:i+8], 2) for i in range(0, eof_index, 8))
            return main_pb2.ExtractResponse(secret_decrypted=secret_bytes)
        except Exception as e:
            return main_pb2.ExtractResponse(error = str(e))

def serve():
        server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
        main_pb2_grpc.add_SteganographyServiceServicer_to_server(StegServicer(), server)
        server.add_insecure_port('[::]:50051')
        server.start()
        server.wait_for_termination()
if __name__ == '__main__':
    try:
        serve()
    except Exception as e:
        print(f"Ошибка запуска: {e}")
        import traceback
        traceback.print_exc()