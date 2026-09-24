import com.tfg.capture.RgbaYuv;
import java.nio.ByteBuffer;
import java.util.Arrays;

public final class PruebasRgbaYuv {
    private static void equal(int actual, int expected, String label) {
        if (actual != expected) throw new AssertionError(label + ": " + actual + " != " + expected);
    }
    public static void main(String[] args) {
        // Cuatro cuadrantes: verde/amarillo arriba, rojo/azul abajo, cada bloque 2x2.
        byte[] rgba = new byte[4*4*4];
        for (int y=0; y<4; y++) for (int x=0; x<4; x++) {
            int p=(y*4+x)*4;
            rgba[p]=(byte)(x<2 ? (y<2 ? 255:0) : (y<2 ? 0:255));
            rgba[p+1]=(byte)(y<2 ? 0:255);
            rgba[p+2]=(byte)(x>=2 && y<2 ? 255:0);
            rgba[p+3]=(byte)255;
        }
        for (boolean interleaved : new boolean[]{false,true}) {
            ByteBuffer y=ByteBuffer.allocate(40), u=ByteBuffer.allocate(24), v;
            Arrays.fill(y.array(),(byte)7); Arrays.fill(u.array(),(byte)7);
            y.position(2); u.position(1);
            if(interleaved) { v=u.duplicate(); v.position(2); }
            else { v=ByteBuffer.allocate(24); v.position(1); }
            int step=interleaved ? 2:1, vo=interleaved ? 2:1;
            RgbaYuv.convert(rgba,4,4,new RgbaYuv.Plane(y,8,1),new RgbaYuv.Plane(u,8,step),new RgbaYuv.Plane(v,8,step));
            equal(y.get(2)&255,144,"Verde arriba izquierda");
            equal(y.get(4)&255,210,"Amarillo arriba derecha");
            equal(y.get(18)&255,82,"Rojo abajo izquierda");
            equal(y.get(20)&255,41,"Azul abajo derecha");
            equal(u.get(1)&255,54,"U verde"); equal(v.get(vo)&255,34,"V verde");
            equal(u.get(9+step)&255,240,"U azul"); equal(v.get(vo+8+step)&255,110,"V azul");
            equal(y.get(0)&255,7,"Respeta offset"); equal(y.get(6)&255,7,"Respeta padding");
            equal(y.position(),2,"No modifica posición buffer");
        }
        boolean rejected=false;
        try { RgbaYuv.convert(rgba,3,4,null,null,null); } catch(IllegalArgumentException e) { rejected=true; }
        if(!rejected) throw new AssertionError("Dimensiones impares aceptadas");
        System.out.println("PASS: YUV planar e intercalado, color, orden de ojos, orientación, offset, padding y dimensiones.");
    }
}
